const getHTML = () => {
   const html = {};
   document.querySelectorAll('*[id]').forEach(e => html[e.id] = e);
   return html;
};

const html = getHTML();

const canv = html.mainCanvas;
const ctx = canv.getContext('2d');

const zoom = 10; // image magnification
const gap = 1; // between magnified pixels
const space = 20; // between images
let iw, ih, iData, iData2, iDataDf;

const drawPixel = (row, col, offX, offY, zoom, iData) => {
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
      drawPixel(row, col, offX, offY, zoom, iData);
   }
};

window.electronAPI.onFilePath(obj => {

   const fpath = obj.href;

   console.log(`Loading image '${fpath}'...`);

   new Promise(res => {
      const img = new Image();
      img.onload = () => { res(img) };
      img.src = fpath;
   })
      .then(imgSrc => { onload(imgSrc, obj.fdat) })
      .catch(err => { console.error('Loading image failed: ', err) });
});

const onload = (imgSrc, fData) => {

   ctx.clearRect(0, 0, canv.width, canv.height);

   iw = imgSrc.width;
   ih = imgSrc.height;
   ctx.drawImage(imgSrc, 0, 0);
   const canvData = ctx.getImageData(0, 0, iw, ih);
   iData = canvData.data;

   console.log(`width: ${iw}, height: ${ih}`);

   let iOffX = iw + space;
   let iOffY = 0;
   drawZoomedImg(iOffX, iOffY, iData);
   iData2 = new Uint8ClampedArray(fData);

   iOffX += iw * (zoom + gap) + space;
   drawZoomedImg(iOffX, iOffY, iData2);

   const iDataPix = new Int32Array(iData.buffer);
   const iDataPix2 = new Int32Array(iData2.buffer);

   iDataDf = new Uint8ClampedArray(iData.length);

   for (let i = 0; i < iDataPix.length; i++) {
      const r = 4 * i;
      const a = r + 3;
      iDataDf[r] = iDataPix[i] == iDataPix2[i] ? 48 : 255;
      iDataDf[a] = 255;
   }

   iOffX += iw * (zoom + gap) + space;
   drawZoomedImg(iOffX, iOffY, iDataDf);

   const info = document.getElementById('info');

   canv.addEventListener('click', evt => {
      const [x, y] = [evt.clientX, evt.clientY];
      const cRect = canv.getBoundingClientRect();
      if (
         x < cRect.left + iOffX ||
         x > cRect.right + iOffX ||
         y < cRect.top + iOffY ||
         y > cRect.bottom + iOffY) return;

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
