//const http = require('http');
//const port = 8080;

const http = require('https');
const port = 443;

const fs = require('fs');
const path = require('path');

const hostname = 'localhost';

const server = http.createServer(
   {
      key: fs.readFileSync('server.key'),
      cert: fs.readFileSync('server.crt')
   },
   (req, res) => {
   // Parse the URL to determine the requested file path
   const filePath = '.' + req.url; console.log(filePath);
   const extname = path.extname(filePath);
   const contentType = getContentType(extname);

   // Read the file from the hard drive and return it as the server response
   fs.readFile(filePath, (err, content) => {
      if (err) {
         if (err.code === 'ENOENT') {
            // Handle file not found errors
            res.writeHead(404, { 'Content-Type': 'text/plain' });
            res.end('File not found');
         } else {
            // Handle other types of errors
            res.writeHead(500, { 'Content-Type': 'text/plain' });
            res.end(`Server error: ${err.code}`);
         }
      } else {
         // Send the file content as the server response
         res.setHeader('Access-Control-Allow-Origin', '*');
         // res.setHeader('Access-Control-Allow-Origin', `http://${hostname}:${port}/`);
         // res.setHeader('Access-Control-Allow-Credentials', true);
         res.writeHead(200, { 'Content-Type': contentType });
         res.end(content, 'utf-8');
      }
   });
});

// Helper function to determine the content type based on the file extension
function getContentType(extname) {
   switch (extname) {
      case '.html':
         return 'text/html';
      case '.js':
         return 'text/javascript';
      case '.css':
         return 'text/css';
      case '.json':
         return 'application/json';
      case '.png':
         return 'image/png';
      case '.jpg':
      case '.jpeg':
         return 'image/jpeg';
      case '.xdat':
         return 'application-octet';
      default:
         return 'text/plain';
   }
}

server.listen(port, hostname, () => {
   console.log(`Server running at http://${hostname}:${port}/`);
});
