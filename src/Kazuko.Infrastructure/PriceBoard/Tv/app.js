(function(){'use strict';
var current=null,page=0,failures=0,lastPageChange=Date.now();
function el(id){return document.getElementById(id);}function text(id,value){el(id).textContent=value||'';}
function draw(){if(!current)return;var s=current,rows=s.active&&failures<3?s.rows:[],limit=s.rowsPerPage||8,pages=Math.max(1,Math.ceil(rows.length/limit));page=page%pages;
document.body.style.backgroundColor=s.backgroundColor;text('shop',s.shopName||'BẢNG GIÁ VÀNG');el('shop').style.color=s.titleColor;text('ticker',s.ticker);el('ticker').style.color=s.titleColor;
text('updated','Giá cập nhật: '+(s.updatedLabel||'—'));text('unit','Đơn vị: '+s.unitLabel);text('status',failures?'Mất kết nối tới ERP — đang thử lại. Giá có thể đã cũ.':(s.status||''));text('page',pages>1?(page+1)+' / '+pages:'');
var body=el('rows');while(body.firstChild)body.removeChild(body.firstChild);var slice=rows.slice(page*limit,(page+1)*limit);
for(var i=0;i<slice.length;i++){var r=document.createElement('tr'),values=[slice[i].name,slice[i].buy,slice[i].sell];for(var j=0;j<3;j++){var cell=document.createElement('td');cell.textContent=values[j];cell.style.borderColor=s.borderColor;if(j>0){cell.style.color=s.priceColor;cell.style.textAlign=s.alignment;}r.appendChild(cell);}body.appendChild(r);}
var heads=document.getElementsByTagName('th');for(var h=0;h<heads.length;h++)heads[h].style.borderColor=s.borderColor;
el('prices').style.display=rows.length?'block':'none';el('empty').style.display=rows.length?'none':'flex';text('empty',failures>=3?'Không nhận được giá mới. Kiểm tra ERP và kết nối mạng.':(s.active?'Chưa có loại vàng được chọn hiển thị.':'Bảng giá đang tạm dừng.'));
var priceHeight=el('prices').clientHeight,bodyFont=Math.max(12,Math.min(window.innerWidth*.034,(priceHeight-window.innerHeight*.06)/Math.max(slice.length,1)*.56));
var cells=body.getElementsByTagName('td');for(var c=0;c<cells.length;c++)cells[c].style.fontSize=(c%3===0?bodyFont*.82:bodyFont)+'px';
}
function load(){var xhr=new XMLHttpRequest();xhr.open('GET','data?t='+Date.now(),true);xhr.timeout=5000;xhr.onreadystatechange=function(){if(xhr.readyState!==4)return;if(xhr.status===200){try{current=JSON.parse(xhr.responseText);failures=0;draw();}catch(e){failed();}}else failed();};xhr.ontimeout=failed;xhr.onerror=failed;xhr.send();}
function failed(){failures++;text('status','Mất kết nối tới ERP — đang thử lại. Giá hiển thị có thể đã cũ.');if(failures>=3){el('prices').style.display='none';el('empty').style.display='flex';text('empty','Không nhận được giá mới. Kiểm tra ERP và kết nối mạng.');}}
el('fullscreen').onclick=function(){var root=document.documentElement,request=root.requestFullscreen||root.webkitRequestFullscreen||root.msRequestFullscreen;if(request)request.call(root);};
window.addEventListener('resize',draw);setInterval(function(){if(Date.now()-lastPageChange>=12000){page++;lastPageChange=Date.now();draw();}},1000);setInterval(load,3000);load();
document.addEventListener('keydown',function(e){if(e.key==='Escape'&&window.chrome&&window.chrome.webview)window.chrome.webview.postMessage('close-tv');});
}());
