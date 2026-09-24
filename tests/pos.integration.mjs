// Run only against a disposable PostgreSQL database: POS_TEST_CONNECTION must be set.
import assert from 'node:assert/strict';
import {randomUUID, randomBytes} from 'node:crypto';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import path from 'node:path';

const connection=process.env.POS_TEST_CONNECTION;
if(!connection)throw new Error('Set POS_TEST_CONNECTION to a disposable PostgreSQL database.');
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const port=Number(process.env.POS_TEST_PORT||5188),origin=`http://127.0.0.1:${port}`;
const password=randomBytes(24).toString('base64url'),phone=`test-${randomUUID()}`;
const app=spawn(process.env.DOTNET||'dotnet',['run','--no-build','--project',path.join(root,'backend/DadoHome.Api')],{
  env:{...process.env,ASPNETCORE_URLS:origin,ConnectionStrings__DadoDb:connection,Jwt__Key:randomBytes(64).toString('base64'),Jwt__Issuer:'PosTests',Jwt__Audience:'PosTests',BootstrapAdmin__Phone:phone,BootstrapAdmin__Password:password},stdio:['ignore','pipe','pipe']
});
let logs='';app.stdout.on('data',d=>logs+=d);app.stderr.on('data',d=>logs+=d);
async function req(url,token,body,method=body?'POST':'GET'){
  const res=await fetch(origin+url,{method,headers:{'Content-Type':'application/json',...(token?{Authorization:`Bearer ${token}`}:{})},body:body?JSON.stringify(body):undefined});
  const raw=await res.text();let data;try{data=JSON.parse(raw);}catch{data=raw;}return {status:res.status,data};
}
async function ok(url,token,body,method){const r=await req(url,token,body,method);assert.ok(r.status<300,`${url}: ${r.status} ${JSON.stringify(r.data)}`);return r.data;}
const base='/api/admin/pos';
try{
  for(let i=0;i<120;i++){try{if((await fetch(origin+'/health')).ok)break;}catch{}if(app.exitCode!==null)throw new Error(logs);await new Promise(r=>setTimeout(r,250));}
  const admin=(await ok('/api/admin/auth/login',null,{phone,password})).accessToken;
  const accounts={Administrator:admin};const users={};
  for(const role of ['Cashier','Finance','OrderManager','Support']){
    const staff=await ok('/api/admin/staff',admin,{name:role,phone:`${role}-${randomUUID()}`,role,password});users[role]=staff;
    accounts[role]=(await ok('/api/admin/auth/login',null,{phone:staff.phone,password})).accessToken;
  }
  const cashier=accounts.Cashier;
  assert.equal((await ok('/api/admin/me',cashier)).role,'Cashier');
  const product=async(stock=10)=>ok('/api/admin/products',admin,{name:`POS test ${randomUUID()}`,categoryId:null,description:'',imageUrl:'',sizes:'',colors:'',price:10,stock,isActive:true});
  const p=await product(),empty=await product(0);
  const payload=(items,extra={})=>({operationId:randomUUID(),items,paymentMethod:'Cash',tendered:100,heldReceiptId:null,...extra});
  const stock=async(id)=>(await ok('/api/products',null)).find(p=>p.id===id).stock;
  assert.equal((await req(base+'/sales',cashier,payload([{productId:p.id,quantity:1}]))).status,409);
  const opening={id:randomUUID(),openingCash:100};
  const opened=await Promise.all([ok(base+'/shifts',cashier,opening),ok(base+'/shifts',cashier,opening)]);
  assert.equal(opened[0].id,opened[1].id);
  assert.equal((await req(base+'/shifts',cashier,{...opening,openingCash:90})).status,409);
  assert.equal((await req(base+'/shifts',cashier,{id:randomUUID(),openingCash:0})).status,409);
  for(const role of ['Finance','OrderManager','Support']){
    assert.equal((await req(base+'/shifts',accounts[role],{id:randomUUID(),openingCash:0})).status,403);
    assert.equal((await req(`${base}/shifts/${opening.id}/close`,accounts[role],{countedCash:100})).status,403);
  }
  for(const role of ['OrderManager','Support'])assert.equal((await req(base+'/shifts',accounts[role])).status,403);
  for(const role of ['OrderManager','Support']){
    for(const endpoint of ['/receipts','/report'])assert.equal((await req(base+endpoint,accounts[role])).status,403);
  }
  for(const role of ['Finance','OrderManager','Support']){
    for(const endpoint of ['/sales','/held'])assert.equal((await req(base+endpoint,accounts[role],payload([{productId:p.id,quantity:1}]))).status,403);
    assert.equal((await req(`${base}/held/${randomUUID()}`,accounts[role],null,'DELETE')).status,403);
    assert.equal((await req(`${base}/receipts/${randomUUID()}/returns`,accounts[role],{operationId:randomUUID(),lineId:randomUUID(),quantity:1})).status,403);
  }
  assert.equal((await req(base+'/receipts')).status,401);
  assert.equal((await req(base+'/report',cashier)).status,403);
  const failed=await req(base+'/sales',cashier,payload([{productId:p.id,quantity:1},{productId:empty.id,quantity:1}]));
  assert.equal(failed.status,409);assert.equal(await stock(p.id),10);
  assert.equal((await req(base+'/sales',cashier,payload([{productId:p.id,quantity:2}],{tendered:1}))).status,409);
  assert.equal(await stock(p.id),10); // rollback after stock has been changed in the transaction
  assert.equal((await req(base+'/sales',cashier,payload([null]))).status,400);
  assert.equal((await req(base+'/sales',cashier,{...payload([{productId:p.id,quantity:1}]),pan:'not-a-card'})).status,400);
  assert.equal((await req(base+'/sales',cashier,payload([{productId:p.id,quantity:1},{productId:p.id,quantity:1}]))).status,400);
  const sale=payload([{productId:p.id,quantity:3}],{tendered:50});
  const parallel=await Promise.all([ok(base+'/sales',cashier,sale),ok(base+'/sales',cashier,sale)]);
  assert.equal(parallel[0].id,parallel[1].id);assert.equal(await stock(p.id),7);
  assert.equal((await req(base+'/sales',cashier,{...sale,tendered:60})).status,409);
  const receipt=parallel[0];assert.equal(receipt.total,30);assert.equal(receipt.lines.length,1);
  assert.equal((await ok(`${base}/receipts/${receipt.id}`,accounts.Finance)).receipt.id,receipt.id);
  const other=await ok('/api/admin/staff',admin,{name:'Other',phone:randomUUID(),role:'Cashier',password});
  const otherToken=(await ok('/api/admin/auth/login',null,{phone:other.phone,password})).accessToken;
  assert.equal((await req(`${base}/shifts/${opening.id}`,otherToken)).status,404);
  assert.equal((await req(`${base}/shifts/${opening.id}/close`,otherToken,{countedCash:100})).status,404);
  assert.equal((await req(`${base}/receipts/${receipt.id}`,otherToken)).status,404);
  const ret={operationId:randomUUID(),lineId:receipt.lines[0].id,quantity:2};
  const returned=await ok(`${base}/receipts/${receipt.id}/returns`,cashier,ret);
  assert.equal(returned.status,'PartiallyReturned');assert.equal(await stock(p.id),9);
  await ok(`${base}/receipts/${receipt.id}/returns`,cashier,ret);assert.equal(await stock(p.id),9);
  const competing=await Promise.all([1,2].map(()=>req(`${base}/receipts/${receipt.id}/returns`,cashier,{...ret,operationId:randomUUID(),quantity:1})));
  assert.deepEqual(competing.map(x=>x.status).sort(),[200,409]);assert.equal(await stock(p.id),10);
  const held=await ok(base+'/held',cashier,payload([{productId:p.id,quantity:2}],{tendered:0}));
  const updated=await ok(base+'/held',cashier,payload([{productId:p.id,quantity:2}],{heldReceiptId:held.id,tendered:0}));
  assert.equal(updated.id,held.id);assert.equal(updated.lines.length,1);
  assert.equal(await stock(p.id),10);
  const completed=await ok(base+'/sales',cashier,payload([{productId:p.id,quantity:2}],{heldReceiptId:held.id,tendered:20}));
  assert.equal(completed.id,held.id);assert.equal(await stock(p.id),8);
  assert.equal((await req(base+'/sales',cashier,payload([{productId:p.id,quantity:2}],{heldReceiptId:held.id}))).status,409);
  const cancel=await ok(base+'/held',cashier,payload([{productId:p.id,quantity:1}],{tendered:0}));
  await ok(`${base}/held/${cancel.id}`,cashier,null,'DELETE');assert.equal(await stock(p.id),8);
  const last=await product(1);
  const race=await Promise.all([1,2].map(()=>req(base+'/sales',cashier,payload([{productId:last.id,quantity:1}],{tendered:10}))));
  assert.deepEqual(race.map(x=>x.status).sort(),[200,409]);assert.equal(await stock(last.id),0);
  const report=await ok(base+'/report',accounts.Finance);assert.equal(report.gross,60);assert.equal(report.returned,30);assert.equal(report.net,30);
  const audit=await ok('/api/audit',admin);assert.equal(audit.filter(x=>x.action==='POS_SALE_COMPLETED').length,3);assert.equal(audit.filter(x=>x.action==='POS_RETURN_COMPLETED').length,2);
  const totals=await ok(`${base}/shifts/${opening.id}`,accounts.Finance);
  assert.equal(totals.cash,60);assert.equal(totals.cashReturns,30);assert.equal(totals.expectedCash,130);
  const closingRace=await Promise.all([
    req(base+'/sales',cashier,payload([{productId:p.id,quantity:1}],{tendered:10})),
    req(`${base}/shifts/${opening.id}/close`,cashier,{countedCash:130})
  ]);
  assert.equal(closingRace[1].status,200);assert.ok([200,409].includes(closingRace[0].status));
  const expected=closingRace[0].status===200?140:130;
  assert.equal(closingRace[1].data.expectedCash,expected);
  assert.equal(closingRace[1].data.difference,130-expected);
  assert.equal((await ok(`${base}/shifts/${opening.id}/close`,cashier,{countedCash:130})).expectedCash,expected);
  assert.equal((await req(`${base}/shifts/${opening.id}/close`,cashier,{countedCash:131})).status,409);
  assert.equal((await req(base+'/sales',cashier,payload([{productId:p.id,quantity:1}]))).status,409);
  assert.equal((await ok(base+'/sales',cashier,sale)).id,receipt.id); // replay remains possible after close
  const nextShift=await ok(base+'/shifts',cashier,{id:randomUUID(),openingCash:50});
  await ok(`${base}/receipts/${completed.id}/returns`,cashier,{operationId:randomUUID(),lineId:completed.lines[0].id,quantity:1});
  const cardSale=await ok(base+'/sales',cashier,payload([{productId:p.id,quantity:1}],{paymentMethod:'Card',tendered:10}));
  await ok(`${base}/receipts/${cardSale.id}/returns`,cashier,{operationId:randomUUID(),lineId:cardSale.lines[0].id,quantity:1});
  await ok(base+'/sales',cashier,payload([{productId:p.id,quantity:1}],{paymentMethod:'QR',tendered:10}));
  const nextTotals=await ok(`${base}/shifts/${nextShift.id}`,cashier);
  assert.equal(nextTotals.expectedCash,40);assert.equal(nextTotals.card,10);assert.equal(nextTotals.cardReturns,10);assert.equal(nextTotals.qr,10);
  assert.equal((await ok(`${base}/shifts/${opening.id}`,cashier)).expectedCash,expected);
  assert.equal((await ok(`${base}/shifts/${nextShift.id}/close`,admin,{countedCash:40})).difference,0);
  await ok(`/api/admin/staff/${users.Cashier.id}`,admin,{...users.Cashier,role:'Support',password:null},'PUT');
  assert.equal((await req(base+'/receipts',cashier)).status,403);
  await ok(`/api/admin/staff/${other.id}`,admin,{...other,isActive:false,password:null},'PUT');
  assert.equal((await req(base+'/receipts',otherToken)).status,403);
  console.log('PASS: POS regression plus shift RBAC, idempotent opening/closing, close/sale race, cash/card/QR totals and cross-shift returns.');
}catch(error){console.error(logs.slice(-12000));throw error;}finally{app.kill();}

