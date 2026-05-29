// Extract tool name -> Description from the MCP server [McpServerTool] annotations.
// Writes scripts/tool-descriptions.json.
import fs from 'node:fs';

const dir = 'src/RevitCortex.Server/Tools';
const files = fs.readdirSync(dir).filter(f => f.endsWith('.cs'));
const map = {};

// Match: [McpServerTool(Name = "x"), Description("....")]  (Description string may contain escaped quotes)
const re = /\[McpServerTool\(Name\s*=\s*"([a-z_0-9]+)"\)\s*,\s*Description\("((?:[^"\\]|\\.)*)"\)\]/g;

for (const f of files) {
  const t = fs.readFileSync(`${dir}/${f}`, 'utf8');
  let m;
  while ((m = re.exec(t))) {
    map[m[1]] = m[2].replace(/\\"/g, '"');
  }
}

const names = Object.keys(map).sort();
fs.writeFileSync('scripts/tool-descriptions.json', JSON.stringify(map, null, 1));
console.log('extracted:', names.length);

const schema = (fs.readFileSync('tool-schemas.txt', 'utf8').match(/^[a-z_0-9]+(?=\()/gm) || []);
const missing = schema.filter(n => !map[n]);
console.log('in schema but no description (' + missing.length + '):', missing.join(', '));
