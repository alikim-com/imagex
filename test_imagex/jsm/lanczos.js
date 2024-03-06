const pi = Math.PI;
const piSq = pi * pi;

const lanczos_kernel = (x, a) => {
   if (x == 0)
      return 1;
   else if (Math.abs(x) >= a)
      return 0;
   else
      return a * Math.sin(pi * x) * Math.sin(pi * x / a) / (piSq * x * x);
};

const lanczos_downsample = (iData, width, height, new_width, new_height, a = 3) => {
  
   const [ratX, ratY] = [width / new_width, height / new_height];

   const iDataR = new Uint8ClampedArray(new_width * new_height);

   const a = 3;

   for (let y = 0; y < new_height; y++) {
      const iOffY = y * new_width;
      const scY = ratY * y;
      for (let x = 0; x < new_width; x++) {
         let sum_val = 0;
         let sum_weight = 0;
         for (let i = scY - a; i < scY + a + 1; i++) {
            const weight_y = lanczos_kernel((scY - i) / ratY);
            if (i < 0 || i >= height) continue;
            for (let j = scX - a; j < scX + a + 1; j++) {
               if (0 <= j && j < width) {
                  const weight_x = lanczos_kernel((scX - j) / ratX);
                  const weight = weight_y * weight_x;
                  sum_val += image[i, j] * weight;
                  sum_weight += weight;
               }
            }
         }
         iDataR[iOffY + x] = sum_weight > 0 ? sum_val / sum_weight : 0;
      }
   }
   return iDataR;
};

export { lanczos_kernel, lanczos_downsample };