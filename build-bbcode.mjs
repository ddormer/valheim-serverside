import markdown_to_bbcode from 'markdown-to-bbcode';
import { Parser } from 'commonmark';

import { readFile } from 'fs';

function render_bbcode(data) {
    var reader = new Parser();
    var parsed = reader.parse(data);
    
    var renderer = markdown_to_bbcode.BBCodeRenderer({"newline_after_heading": false});
    var result = writer.render(parsed);
    console.log(result);
}

readFile('README.md', function (err, data)
{
    if (err) {
        return console.log(err);
    }
    render_bbcode(data);
})