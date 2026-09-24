// Tests an actual self-contained kit. Uses only disposable pilot demo data.
// PILOT_KIT_ROOT must point at the built test copy, not a working store installation.
import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFileSync} from 'node:fs';
import path from 'node:path';
import {randomUUID} from 'node:crypto';

const root=process.env.PILOT_KIT_ROOT;
if(!root)throw new Error('Set PILOT_KIT_ROOT to a disposable built pilot directory.');
const script=name=>execFileSync('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File',path.join(root,'scripts',name),...(name==='Start.ps1'?['-NoBrowser']:[])],{stdio:'inherit',timeout:90000});
let settings,origin;
async function request(url,token,body){
  const r=await fetch(origin+url,{method:body?'POST':'GET',headers:{Connection:'close','Content-Type':'application/json',...(token?{Authorization:`Bearer ${token}`}:{})},body:body?JSON.stringify(body):undefined});
  const text=await r.text();let data;try{data=JSON.parse(text);}catch{data=text;}return {status:r.status,data};
}
async function ok(url,token,body){const r=await request(url,token,body);assert.ok(r.status<300,`${url}: ${r.status}`);return r.data;}
async function login(index,role){return (await ok('/api/admin/auth/login',null,{phone:`+99290000000${index}`,password:settings.Passwords[role]})).accessToken;}
try{
  script('Start.ps1');
  settings=JSON.parse(readFileSync(path.join(root,'runtime/settings.json'),'utf8').replace(/^\uFEFF/,''));
  origin=`http://127.0.0.1:${settings.WebPort}`;
  assert.equal((await ok('/health/ready')).pilot,true);
  const html=await ok('/');assert.match(html,/DADO Home/);
  const asset=html.match(/src="([^"]+\.js)"/)[1];const js=await ok(asset);assert.ok(!js.includes('http://localhost:5000'));
  const admin=await login(1,'Administrator'),cashier=await login(2,'Cashier'),finance=await login(3,'Finance');
  const manager=await login(4,'OrderManager'),support=await login(5,'Support');
  const staff=await ok('/api/admin/staff',admin);assert.equal(staff.length,5);
  const products=await ok('/api/products');assert.equal(products.length,8);
  const mug=products.find(p=>p.name==='Кружка керамическая');
  const current=await ok('/api/admin/pos/shifts/current',cashier);
  if(!current.shift)await ok('/api/admin/pos/shifts',cashier,{id:randomUUID(),openingCash:0});
  const sale={operationId:randomUUID(),items:[{productId:mug.id,quantity:2}],paymentMethod:'Cash',tendered:100,heldReceiptId:null};
  const receipt=await ok('/api/admin/pos/sales',cashier,sale);assert.equal(receipt.total,70);
  assert.equal((await ok('/api/products')).find(p=>p.id===mug.id).stock,mug.stock-2);
  const result=await ok(`/api/admin/pos/receipts/${receipt.id}/returns`,cashier,{operationId:randomUUID(),lineId:receipt.lines[0].id,quantity:1});assert.equal(result.status,'PartiallyReturned');
  assert.equal((await request('/api/admin/pos/sales',finance,sale)).status,403);
  for(const token of [manager,support])assert.equal((await request('/api/admin/pos/receipts',token)).status,403);
  const expense={id:randomUUID(),date:'2026-09-25',amount:75.50,category:'Транспорт',description:'Тест комплекта: доставка в магазин'};
  await ok('/api/admin/expenses',admin,expense);
  assert.equal((await ok('/api/admin/expenses',finance)).total,75.50);
  assert.equal((await request('/api/admin/expenses',finance,{...expense,id:randomUUID()})).status,403);
  for(const token of [cashier,manager,support])assert.equal((await request('/api/admin/expenses',token)).status,403);
  assert.equal((await request('/api/auth/otp/request',null,{phone:'+992900001111'})).status,503);
  script('Start.ps1'); // repeated start must reuse the running instance
  script('Stop.ps1');
  script('Start.ps1');
  const saved=JSON.parse(readFileSync(path.join(root,'runtime/settings.json'),'utf8').replace(/^\uFEFF/,''));assert.deepEqual(saved,settings);
  const cashierAgain=await login(2,'Cashier');
  const replay=await ok('/api/admin/pos/sales',cashierAgain,sale);assert.equal(replay.id,receipt.id);
  assert.equal((await ok('/api/products')).find(p=>p.id===mug.id).stock,mug.stock-1);
  assert.equal((await ok('/api/admin/staff',await login(1,'Administrator'))).length,5);
  assert.equal((await ok('/api/products')).length,8);
  const adminAgain=await login(1,'Administrator');
  const expenses=await ok('/api/admin/expenses',adminAgain);
  assert.equal(expenses.total,75.50);assert.equal(expenses.items[0].id,expense.id);
  await ok(`/api/admin/expenses/${expense.id}/cancel`,adminAgain,{reason:'Проверка отмены'});
  assert.equal((await ok('/api/admin/expenses',adminAgain)).total,0);
  console.log('PASS: standalone Windows startup, same-origin frontend, five roles, demo catalog, sale/return, repeated start, restart persistence and idempotency.');
}catch(error){
  if(error.stdout)console.error(error.stdout.toString());
  if(error.stderr)console.error(error.stderr.toString());
  throw error;
}finally{try{script('Stop.ps1');}catch{}}


