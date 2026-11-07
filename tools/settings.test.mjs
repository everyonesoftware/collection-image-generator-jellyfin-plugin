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
  const state = {failLoad:false,failSave:false,writes:0,loading:0};
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
      async updatePluginConfiguration(id,value){if(state.failSave)throw Error('Mock save failure');stored=structuredClone(value);state.writes++;return{};}
    }
  };
  vm.runInNewContext(script, context, {filename:'embedded-configPage.js'});
  return {state,button,field:id=>elements.get('#'+id),get stored(){return stored;},
    load:()=>page.events.get('pageshow')(),
    save:()=>form.events.get('submit')({preventDefault(){}})};
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

test('oversized dimensions never reach update API',async()=>{
  const f=fixture();await f.load();f.field('Width').value='4096';f.field('Height').value='4096';await f.save();
  assert.equal(f.state.writes,0);assert.match(f.field('CollectionImageGeneratorStatus').textContent,/8,388,608/);
});

test('load failure leaves save disabled and refuses submission',async()=>{
  const f=fixture();f.state.failLoad=true;await f.load();await f.save();
  assert.equal(f.button.disabled,true);assert.equal(f.state.writes,0);assert.match(f.field('CollectionImageGeneratorStatus').textContent,/Could not load/);assert.equal(f.state.loading,0);
});

test('save failure reports error and re-enables save without changing persisted config',async()=>{
  const f=fixture();await f.load();f.state.failSave=true;f.field('TextTemplate').value='not saved';await f.save();
  assert.equal(f.button.disabled,false);assert.equal(f.state.writes,0);assert.equal(f.stored.TextTemplate,'{name}');
  assert.match(f.field('CollectionImageGeneratorStatus').textContent,/could not be saved/);assert.equal(f.state.loading,0);
});
