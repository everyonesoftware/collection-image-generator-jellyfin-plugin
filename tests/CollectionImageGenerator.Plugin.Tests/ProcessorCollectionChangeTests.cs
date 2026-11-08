using System.Reflection;
using MediaBrowser.Controller.Entities.Movies;
using SkiaSharp;
using Xunit;

namespace CollectionImageGenerator.Plugin.Tests;

public sealed partial class ProcessorTests
{
    private Task Changed(CollectionProcessor processor, params Guid[] ids)
    {
        var method = typeof(CollectionProcessor).GetMethod("RunChangedCollectionsAsync");
        Assert.NotNull(method);
        return (Task)method.Invoke(processor, [ids, CancellationToken.None])!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_run_rechecks_live_flags_after_waiting_for_shared_gate(bool disablePlugin)
    {
        var flag = typeof(PluginConfiguration).GetProperty("RunOnCollectionChanges");
        Assert.NotNull(flag);
        Assert.True((bool)flag.GetValue(config)!);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        library.BeforeSave = async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
        var processor = Processor();
        var full = Run(processor);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var id = library.Collections[0].Id;
        var pending = Changed(processor, id);
        if (disablePlugin) config.Enabled = false;
        else flag.SetValue(config, false);
        var reloads = 0;
        library.OnReload = _ => reloads++;
        release.SetResult();
        await Task.WhenAll(full, pending);
        Assert.Equal(0, reloads);
        Assert.Equal(1, library.Saves);
    }

    [Fact]
    public async Task Changed_run_isolates_reload_failure_and_still_updates_other_ids()
    {
        var failed = library.Collections[0].Id;
        var other = new BoxSet { Id = Guid.NewGuid(), Name = "Other" };
        library.Collections.Add(other);
        library.OnReload = item => { if (item.Id == failed) throw new IOException("Deleted during hydration"); };
        var processor = Processor();
        var error = await Assert.ThrowsAsync<AggregateException>(() => processor.RunChangedCollectionsAsync([failed, other.Id], default));
        Assert.Single(error.InnerExceptions);
        Assert.Equal(1, library.Saves);
        Assert.NotNull(library.Paths[other.Id]);
    }

    [Fact]
    public async Task Changed_run_preserves_unknown_custom_primary_and_empty_collection_poster()
    {
        var id = library.Collections[0].Id;
        var custom = Path.Combine(directory, "custom-change.png");
        WritePng(custom, SKColors.Blue);
        library.Paths[id] = custom;
        await Changed(Processor(), id);
        Assert.Equal(0, library.Saves);
        var before = File.ReadAllBytes(custom);
        library.Members.Clear();
        config.OverwriteExisting = true;
        await Changed(Processor(), id);
        Assert.Equal(0, library.Saves);
        Assert.Equal(before, File.ReadAllBytes(custom));
    }

    [Fact]
    public async Task Changed_run_skips_unmodified_selected_fingerprint_and_trigger_is_not_render_option()
    {
        var processor = Processor();
        var id = library.Collections[0].Id;
        await Changed(processor, id);
        config.RunOnCollectionChanges = false;
        await Run(processor); // Changing the trigger must not invalidate owned artwork.
        config.RunOnCollectionChanges = true;
        await Changed(processor, id);
        Assert.Equal(1, library.Saves);
        WritePng(library.Paths[library.Members[0].Id]!, SKColors.Green);
        await Changed(processor, id);
        Assert.Equal(2, library.Saves);
    }

    [Fact]
    public async Task Changed_run_missing_or_no_usable_sources_does_not_write()
    {
        var id = library.Collections[0].Id;
        var processor = Processor();
        await Changed(processor, Guid.NewGuid());
        await File.WriteAllTextAsync(library.Paths[library.Members[0].Id]!, "invalid image");
        await Changed(processor, id);
        Assert.Equal(0, library.Saves);
        Assert.False(library.Paths.ContainsKey(id));
    }

    [Fact]
    public async Task Changed_run_cancellation_while_waiting_does_not_release_full_run_gate()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        library.BeforeSave = async (_, ct) => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
        var processor = Processor();
        var full = Run(processor);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var changed = processor.RunChangedCollectionsAsync([library.Collections[0].Id], cancellation.Token);
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => changed);
            Assert.Equal(0, library.Saves);
        }
        finally { release.TrySetResult(); await full; }
        await Changed(processor, library.Collections[0].Id);
        Assert.Equal(1, library.Saves);
    }

    [Fact]
    public async Task Changed_run_membership_change_regenerates_and_last_removal_clears_owned_poster()
    {
        var processor = Processor();
        var id = library.Collections[0].Id;
        await Changed(processor, id);
        var original = Hash(library.Paths[id]!);
        var extra = new Movie { Id = Guid.NewGuid(), Name = "Added member", PremiereDate = new DateTime(1990, 1, 1) };
        var extraPath = Path.Combine(directory, "added-member.png");
        WritePng(extraPath, SKColors.Blue);
        library.Paths[extra.Id] = extraPath;
        library.Members.Add(extra);
        await Changed(processor, id);
        Assert.Equal(2, library.Saves);
        Assert.NotEqual(original, Hash(library.Paths[id]!));
        library.Members.Clear();
        await Changed(processor, id);
        Assert.Equal(2, library.Saves);
        Assert.Equal(1, library.Clears);
        Assert.Null(library.GetPrimaryPath(library.Collections[0]));
    }

    [Fact]
    public async Task Changed_run_targets_only_distinct_existing_ids_without_scanning()
    {
        var other = new BoxSet { Id = Guid.NewGuid(), Name = "Unchanged" };
        library.Collections.Add(other);
        var id = library.Collections[0].Id;
        var reloads = new List<Guid>();
        library.OnReload = item => reloads.Add(item.Id);
        await Changed(Processor(), id, id, Guid.Empty, Guid.NewGuid());
        Assert.Equal(1, library.Saves);
        Assert.False(library.Paths.ContainsKey(other.Id));
        Assert.Equal(new[] { id, id }, reloads);
    }
}
