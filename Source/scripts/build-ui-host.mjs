// Run after npm ci in ../room-service. Reuses esbuild installed by the locked tsx dependency.
import {createRequire} from 'node:module';
import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));
const service=path.resolve(here,'../room-service');
const require=createRequire(path.join(service,'package.json'));
const {build}=require('esbuild');
const output=process.argv[2];
if(!output)throw new Error('Pass a new client build folder as the only argument.');
const folder=path.join(path.resolve(output),'room-service');
fs.mkdirSync(folder,{recursive:true});
const result=await build({entryPoints:[path.join(service,'src/server.ts')],bundle:true,platform:'node',format:'esm',target:'node22',write:false,
    banner:{js:"import { createRequire as __createRequire } from 'node:module';const require=__createRequire(import.meta.url);"},legalComments:'eof'});
fs.writeFileSync(path.join(folder,'room-service.mjs'),result.outputFiles[0].contents);
fs.copyFileSync(path.join(service,'package.json'),path.join(folder,'package-source.json'));
const legal=[];
function scan(dir) {
    for(const entry of fs.readdirSync(dir,{withFileTypes:true})) {
        const full=path.join(dir,entry.name);
        if(entry.isDirectory())scan(full);
        else if(/^license(?:\.|$)|^copying(?:\.|$)/i.test(entry.name))legal.push('FILE: '+path.relative(service,full)+'\n'+fs.readFileSync(full,'utf8'));
    }
}
scan(path.join(service,'node_modules'));
fs.writeFileSync(path.join(folder,'THIRD-PARTY-LICENSES.txt'),legal.join('\n\n'));
console.log('Room service bundled; source and dependency versions unchanged.');
