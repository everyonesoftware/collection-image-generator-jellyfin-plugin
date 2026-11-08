// Executes the real embedded page script; only Jellyfin/browser boundaries are mocked.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import test from 'node:test';
import assert from 'node:assert/strict';

const html = readFileSync(fileURLToPath(new URL('../src/CollectionImageGenerator.Plugin/Configuration/configPage.html', import.meta.url)), 'utf8');
const script = html.match(/<script[^>]*>([\s\S]*?)<\/script>/i)[1];
const defaults = {Enabled:true,RunAfterScan:true,EnableText:false,OverwriteExisting:false,FontSize:80,Width:1000,Height:1500,SelectionMode:0,TextTemplate:'{name}',TextPosition:2,FutureField:'preserve-me'};

function fixture(config = defaults) {
  let stored = structuredClone(config);
  const state = {failLoad:false,failSave:false,failTasks:false,failStart:false,writes:0,loading:0,started:[],
    tasks:[{Id:'unrelated-task',Key:'OtherPlugin',Name:'Generate collection images',State:'Idle'},
           {Id:'collection-images-task',Key:'CollectionImageGenerator',Name:'Localized task name',State:'Idle'}]};
  const elements = new Map();
  for (const [, id] of html.matchAll(/\bid="([^"]+)"/g)) elements.set('#'+id, {value:'',checked:false,disabled:false,textContent:''});
  const button = {disabled:false};
  elements.set('button[type="submit"]', button);
  for (const element of elements.values()) {
    element.events = new Map();
    element.addEventListener = (name, handler) => element.events.set(name, handler);
    element.querySelector = selector => elements.get(selector);
    Object.defineProperty(element, 'valueAsNumber', {get(){return Number(this.value);}});
  }
  const page = elements.get('#CollectionImageGeneratorConfigPage');
  const form = elements.get('#CollectionImageGeneratorConfigForm');
  form.reportValidity = () => true;
  const context = {
    document:{querySelector:selector=>elements.get(selector)},
    Dashboard:{showLoadingMsg(){state.loading++},hideLoadingMsg(){state.loading--},processPluginConfigurationUpdateResult(){}},
    ApiClient:{
      async getPluginConfiguration(id){assert.equal(id,'f8a5df17-f0b4-4eab-9144-a46ea55c2b28');if(state.failLoad)throw Error('Mock load failure');return structuredClone(stored);},
      async updatePluginConfiguration(id,value){state.saveCalls=(state.saveCalls??0)+1;if(state.saveBarrier)await state.saveBarrier;if(state.failSave)throw Error('Mock save failure');stored=structuredClone(value);state.writes++;return{};},
      async getScheduledTasks(){if(state.failTasks)throw Error('Mock task-list failure');return structuredClone(state.tasks);},
      async startScheduledTask(id){if(state.failStart)throw Error('Mock start failure');state.started.push(id);if(state.startBarrier)await state.startBarrier;}
    }
  };
  vm.runInNewContext(script, context, {filename:'embedded-configPage.js'});
  return {state,button,field:id=>elements.get('#'+id),get stored(){return stored;},
    load:()=>page.events.get('pageshow')(),
    save:()=>form.events.get('submit')({preventDefault(){}}),
    run:()=>{const action=elements.get('#GenerateNow');assert.ok(action,'Generate now button is missing');const handler=action.events.get('click');assert.equal(typeof handler,'function','Generate now is not wired');return handler();}};
}

test('loads numeric enums and safe defaults, saves typed fields, preserves unknown fields, then reloads',async()=>{
  const f=fixture();await f.load();
  assert.equal(f.field('SelectionMode').value,'ReleaseDate');assert.equal(f.field('TextPosition').value,'Bottom');
  assert.equal(f.field('EnableText').checked,false);assert.equal(f.field('OverwriteExisting').checked,false);
  f.field('EnableText').checked=true;f.field('SelectionMode').value='StableRandom';f.field('TextPosition').value='Middle';
  f.field('TextTemplate').value='{name} <script>literal</script>';f.field('FontSize').value='72';
  await f.save();assert.equal(f.stored.SelectionMode,'StableRandom');assert.equal(f.stored.TextPosition,'Middle');
  assert.equal(f.stored.FontSize,72);assert.equal(f.stored.FutureField,'preserve-me');
  assert.equal(f.stored.TextTemplate,'{name} <script>literal</script>');assert.equal(f.state.writes,1);
  f.field('TextTemplate').value='unsaved';await f.load();assert.equal(f.field('TextTemplate').value,f.stored.TextTemplate);
  assert.equal(f.state.loading,0);
});

test('older configurations default to simple repeat without an implicit settings save',async()=>{
  const f=fixture();await f.load();
  assert.ok(f.field('RepeatMode'),'Repeat mode dropdown is missing');
  assert.equal(f.field('RepeatMode').value,'SimpleRepeat');
  assert.equal(f.state.writes,0);
});

test('repeat mode handles numeric and named enums and survives save and reload',async()=>{
  const modes=['SimpleRepeat','RepeatCheckered','NoRepeat'];
  for(const [index,mode] of modes.entries()){
    for(const response of [index,mode]){
      const f=fixture({...defaults,RepeatMode:response});await f.load();
      assert.equal(f.field('RepeatMode').value,mode);
      const next=modes[(index+1)%modes.length];f.field('RepeatMode').value=next;
      await f.save();assert.equal(f.stored.RepeatMode,next);assert.equal(f.stored.FutureField,'preserve-me');
      f.field('RepeatMode').value='unsaved';await f.load();assert.equal(f.field('RepeatMode').value,next);
      assert.equal(f.state.loading,0);
    }
  }
});

test('runtime poster orders have both dropdown choices and load numeric or named enums through save and reload',async()=>{
  const select=html.match(/<select\b[^>]*id="SelectionMode"[^>]*>([\s\S]*?)<\/select>/i)?.[1];
  assert.ok(select,'Member poster order dropdown is missing');
  const modes=['ReleaseDate','Alphabetical','StableRandom','RuntimeShortestFirst','RuntimeLongestFirst'];
  for(const mode of modes) assert.match(select,new RegExp('<option\\b[^>]*value="'+mode+'"'));
  for(const [index,mode] of modes.entries()){
    for(const response of [index,mode]){
      const f=fixture({...defaults,SelectionMode:response});await f.load();
      assert.equal(f.field('SelectionMode').value,mode);
      const next=modes[(index+1)%modes.length];f.field('SelectionMode').value=next;
      await f.save();assert.equal(f.stored.SelectionMode,next);assert.equal(f.stored.FutureField,'preserve-me');
      f.field('SelectionMode').value='unsaved';await f.load();assert.equal(f.field('SelectionMode').value,next);
      assert.equal(f.state.loading,0);assert.deepEqual(f.state.started,[]);
    }
  }
});

test('collection-change generation defaults on for older configs and persists an explicit opt-out',async()=>{
  const f=fixture();await f.load();
  assert.ok(f.field('RunOnCollectionChanges'),'Automatic collection-change checkbox is missing');
  assert.equal(f.field('RunOnCollectionChanges').checked,true);assert.equal(f.state.writes,0);
  f.field('RunOnCollectionChanges').checked=false;await f.save();
  assert.equal(f.stored.RunOnCollectionChanges,false);assert.equal(f.stored.FutureField,'preserve-me');
  f.field('RunOnCollectionChanges').checked=true;await f.load();assert.equal(f.field('RunOnCollectionChanges').checked,false);
  f.field('RunOnCollectionChanges').checked=true;await f.save();await f.load();
  assert.equal(f.stored.RunOnCollectionChanges,true);assert.equal(f.field('RunOnCollectionChanges').checked,true);
  assert.deepEqual(f.state.started,[]);assert.equal(f.state.loading,0);
});

test('oversized dimensions never reach update API',async()=>{
  const f=fixture();await f.load();f.field('Width').value='4096';f.field('Height').value='4096';await f.save();
  assert.equal(f.state.writes,0);assert.match(f.field('CollectionImageGeneratorStatus').textContent,/8,388,608/);
});

test('load failure leaves save disabled and refuses submission',async()=>{
  const f=fixture();f.state.failLoad=true;await f.load();await f.save();
  assert.equal(f.button.disabled,true);assert.equal(f.state.writes,0);assert.match(f.field('CollectionImageGeneratorStatus').textContent,/Could not load/);assert.equal(f.state.loading,0);
});

test('Generate now starts only the task with this plugin key and uses saved settings without writing',async()=>{
  const f=fixture();await f.load();
  f.field('TextTemplate').value='unsaved edit';
  await f.run();
  assert.deepEqual(f.state.started,['collection-images-task']);
  assert.equal(f.state.writes,0);
  assert.equal(f.stored.TextTemplate,'{name}');
  assert.match(f.field('CollectionImageGeneratorRunStatus').textContent,/requested/i);
  assert.doesNotMatch(f.field('CollectionImageGeneratorRunStatus').textContent,/complete/i);
  assert.equal(f.state.loading,0);
});

test('Generate now refuses running, cancelling, unknown, or missing task states',async()=>{
  for(const state of ['Running','Cancelling',2,1,'Unexpected',null]){
    const f=fixture();await f.load();f.state.tasks[1].State=state;await f.run();
    assert.deepEqual(f.state.started,[],String(state));
    assert.match(f.field('CollectionImageGeneratorRunStatus').textContent,/already|state/i);
  }
  for(const tasks of [[],[{Id:'wrong-task',Key:'OtherPlugin',State:'Idle'}]]){
    const f=fixture();await f.load();f.state.tasks=tasks;await f.run();
    assert.deepEqual(f.state.started,[]);
    assert.match(f.field('CollectionImageGeneratorRunStatus').textContent,/not found|restart/i);
  }
});

test('Generate now stays disabled until settings load and respects saved disabled configuration',async()=>{
  const f=fixture();assert.equal(f.field('GenerateNow').disabled,true);await f.run();assert.deepEqual(f.state.started,[]);
  f.state.failLoad=true;await f.load();assert.equal(f.field('GenerateNow').disabled,true);await f.run();assert.deepEqual(f.state.started,[]);
  const disabled=fixture({...defaults,Enabled:false});await disabled.load();disabled.field('Enabled').checked=true;
  await disabled.run();assert.deepEqual(disabled.state.started,[]);
  assert.match(disabled.field('CollectionImageGeneratorRunStatus').textContent,/enable.*save/i);
});

test('Generate now catches task-list and start failures and restores controls for retry',async()=>{
  for(const flag of ['failTasks','failStart','failLoad']){
    const f=fixture();await f.load();f.state[flag]=true;await f.run();
    assert.deepEqual(f.state.started,[]);
    assert.match(f.field('CollectionImageGeneratorRunStatus').textContent,/could not.*request/i);
    assert.equal(f.field('GenerateNow').disabled,false);assert.equal(f.button.disabled,false);assert.equal(f.state.loading,0);
    f.state[flag]=false;await f.run();assert.deepEqual(f.state.started,['collection-images-task']);
  }
});

test('Generate now blocks duplicate starts and settings saves while a request is in flight',async()=>{
  const f=fixture();await f.load();let release;
  f.state.startBarrier=new Promise(resolve=>{release=resolve;});
  const pending=f.run();await new Promise(setImmediate);
  assert.equal(f.field('GenerateNow').disabled,true);assert.equal(f.button.disabled,true);
  await f.run();await f.save();assert.deepEqual(f.state.started,['collection-images-task']);assert.equal(f.state.writes,0);
  release();await pending;
  assert.equal(f.field('GenerateNow').disabled,false);assert.equal(f.button.disabled,false);assert.equal(f.state.loading,0);
});

test('a pending settings save blocks Generate now and duplicate saves',async()=>{
  const f=fixture();await f.load();let release;
  f.state.saveBarrier=new Promise(resolve=>{release=resolve;});
  const pending=f.save();await new Promise(setImmediate);
  assert.equal(f.button.disabled,true);assert.equal(f.field('GenerateNow').disabled,true);
  await f.run();await f.save();
  assert.deepEqual(f.state.started,[]);assert.equal(f.state.saveCalls,1);assert.equal(f.state.writes,0);
  release();await pending;
  assert.equal(f.state.writes,1);assert.equal(f.button.disabled,false);assert.equal(f.field('GenerateNow').disabled,false);assert.equal(f.state.loading,0);
});

test('Generate now refuses ambiguous or malformed task discovery without a start request',async()=>{
  const task={Id:'collection-images-task',Key:'CollectionImageGenerator',State:'Idle'};
  for(const tasks of [[task,{...task,Id:'duplicate'}],[{...task,Id:''}],[{Key:task.Key,State:'Idle'}],[{Id:task.Id,Key:task.Key}],{},null]){
    const f=fixture();await f.load();f.state.tasks=tasks;await f.run();
    assert.deepEqual(f.state.started,[]);
    assert.match(f.field('CollectionImageGeneratorRunStatus').textContent,/not found|ambiguous|state/i);
    assert.equal(f.field('GenerateNow').disabled,false);assert.equal(f.state.loading,0);
  }
});

test('Generate now supports an idle numeric enum response',async()=>{
  const f=fixture();await f.load();f.state.tasks[1].State=0;await f.run();
  assert.deepEqual(f.state.started,['collection-images-task']);
});

test('save failure reports error and re-enables save without changing persisted config',async()=>{
  const f=fixture();await f.load();f.state.failSave=true;f.field('TextTemplate').value='not saved';await f.save();
  assert.equal(f.button.disabled,false);assert.equal(f.state.writes,0);assert.equal(f.stored.TextTemplate,'{name}');
  assert.match(f.field('CollectionImageGeneratorStatus').textContent,/could not be saved/);assert.equal(f.state.loading,0);
});
