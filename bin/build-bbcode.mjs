import markdown_to_bbcode from 'markdown-to-bbcode';
import { Parser } from 'commonmark';

import { readFile, writeFile } from 'fs';

function render_bbcode(data) {
    var reader = new Parser();
    var parsed = reader.parse(data);
    
    var writer = new markdown_to_bbcode.BBCodeRenderer({"newline_after_heading": false});
    var result = writer.render(parsed);
    return result;
}

readFile('README.md', 'utf8', function (err, data)
{
    if (err) {
        return console.log(err);
    }
    var bbcode = render_bbcode(data);
    writeFile('README.bbcode', bbcode, function (err) {});
})
