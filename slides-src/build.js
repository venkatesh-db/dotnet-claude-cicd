const pptxgen = require('pptxgenjs');
const html2pptx = require('/Users/venkatesh/.claude/skills/pptx/scripts/html2pptx');
const path = require('path');

async function build() {
  const pptx = new pptxgen();
  pptx.layout = 'LAYOUT_16x9';
  pptx.author = 'Claude';
  pptx.title = 'What\'s the benefit? — dotnet-claude-cicd';

  await html2pptx(path.join(__dirname, '1-title.html'), pptx);
  await html2pptx(path.join(__dirname, '2-benefit.html'), pptx);
  await html2pptx(path.join(__dirname, '3-howitworks.html'), pptx);
  await html2pptx(path.join(__dirname, '4-closing.html'), pptx);

  const outPath = path.join(__dirname, '..', 'Project-Benefit-Slides.pptx');
  await pptx.writeFile({ fileName: outPath });
  console.log('Wrote', outPath);
}

build().catch((err) => { console.error(err); process.exit(1); });
