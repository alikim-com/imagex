const log = console.log;

const updateMsg = msg => { html.fileName.innerText = msg };

const getHTML = () => {
   const html = {};
   document.querySelectorAll('*[id]').forEach(e => html[e.id] = e);
   return html;
};

const html = getHTML();

const canv = html.mainCanvas;
const [cw, ch] = [canv.width, canv.height];
const ctx = canv.getContext(
   '2d',
   { willReadFrequently: true } // CPU rendering
);

let zoom = parseInt(html.zoom.value); // image magnification
let gap = 1; // between magnified pixels
const space = 20; // between images
let iw, ih, iData, iData2, iDataDf;

let imgSrc, fData; // loaded image and rgba-data
let msgLoad = '', msgStatus = ''; // loading messages

const drawPixel = (row, col, offX, offY, iData) => {
   const ind = 4 * (row * iw + col);
   const [r, g, b, a] = iData.slice(ind, ind + 4);
   ctx.fillStyle = `rgba(${r}, ${g}, ${b}, ${a / 255})`;
   ctx.fillRect(
      offX + col * (zoom + gap),
      offY + row * (zoom + gap),
      zoom,
      zoom);
};

const getPixelInfo = (row, col, iData) => {
   const ind = 4 * (row * iw + col);
   const [r, g, b, a] =
      Array.from(iData.slice(ind, ind + 4)).map(ch => ch.toString(16).padStart(2, '0'));
   return `${r} ${g} ${b} ${a}`;
};

const drawZoomedImg = (offX, offY, iData) => {
   const len = iData.length / 4;
   for (let i = 0; i < len; i++) {
      const col = i % iw;
      const row = Math.floor(i / iw);
      drawPixel(row, col, offX, offY, iData);
   }
};

const readInt32BigEndian = (byteArr, off) => 
   (byteArr[off] << 24) |
   (byteArr[off + 1] << 16) |
   (byteArr[off + 2] << 8) |
    byteArr[off + 3];

window.electronAPI.onFilePath(obj => {

   const fpath = obj.href;
   fData = obj.fdat;

   //log(`Loading image '${fpath}'...`);
   msgLoad = `Loading image '${fpath}'...`;
   updateMsg(msgLoad);

   new Promise(res => {
      const img = new Image();
      img.onload = () => { res(img) };
      img.src = fpath;
   })
      .then(_imgSrc => {
         imgSrc = _imgSrc;
         onload();
      })
      .catch(err => {
         msgStatus = `FAIL (${err})`;
         updateMsg(msgLoad + msgStatus);
      });
});

const onload = () => {

   iw = imgSrc.width;
   ih = imgSrc.height;

   const iwZoomed = iw * (zoom + gap) - gap;
   const ihZoomed = ih * (zoom + gap) - gap;

   canv.width = Math.max(cw, (iwZoomed + space )* 3 + iw);
   canv.height = Math.max(ch, ihZoomed);

   ctx.clearRect(0, 0, canv.width, canv.height);

   ctx.drawImage(imgSrc, 0, 0);
   const canvData = ctx.getImageData(0, 0, iw, ih);
   iData = canvData.data; // Uint8Clamped

   //log(`width: ${iw}, height: ${ih}`);
   msgStatus = `OK (${iw}x${ih})`;
   updateMsg(msgLoad + msgStatus);

   let iOffX = iw + space;
   let iOffY = 0;
   drawZoomedImg(iOffX, iOffY, iData);

   iData2 = fData.subarray(8); // Uint8Clamped

   iOffX += iwZoomed + space;
   drawZoomedImg(iOffX, iOffY, iData2);

   //const iDataPix = new Int32Array(iData.buffer);
   //const iDataPix2 = new Int32Array(iData2.buffer.slice(8));

   iDataDf = new Uint8ClampedArray(iData.length);

   for (let i = 0; i < iData.length; i += 4) {
      for (let ch = 0; ch < 4; ch++) {
         const ind = i + ch;
         const dif = Math.abs(iData[ind] - iData2[ind]) * 48;
         iDataDf[ind] = dif;  
      }
      iDataDf[i + 3] = 255;
   }

   iOffX += iwZoomed + space;
   drawZoomedImg(iOffX, iOffY, iDataDf);

   const info = document.getElementById('info');

   canv.addEventListener('click', evt => {
      const [x, y] = [evt.clientX, evt.clientY];
      const cRect = canv.getBoundingClientRect();
      const dfLeft = cRect.left + iOffX;
      const dfTop = cRect.top + iOffY;
      const dfRight = dfLeft + iwZoomed;
      const dfBottom = dfTop + ihZoomed;

      if (x < dfLeft || x > dfRight || y < dfTop || y > dfBottom) return;

      const [cX, cY] = [
         evt.clientX - cRect.x,
         evt.clientY - cRect.y];
      const col = Math.floor((cX - iOffX) / (zoom + gap));
      const row = Math.floor((cY - iOffY) / (zoom + gap));
      const iDataPixInf = getPixelInfo(row, col, iData);
      const iDataPixInf2 = getPixelInfo(row, col, iData2);
      info.value =
         `row: ${row}, col: ${col}\n\n${iDataPixInf}\n${iDataPixInf2}`;
   });
};

html.zoom.addEventListener('input', () => {
   zoom = parseInt(html.zoom.value);
   html.zoomLabel.innerText = `zoom level (x${zoom}):`;
   if(imgSrc && fData) onload();
});

html.gap.addEventListener('change', () => { 
   gap = html.gap.checked ? 1 : 0;
   if(imgSrc && fData) onload();
});