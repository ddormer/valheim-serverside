# markdown-to-bbcode

BBCode renderer for commonmark.js

## Usage

You'll need commonmark.js and markdown-to-bbcode.

### importing the module
```
import { BBCodeRenderer } from 'markdown-to-bbcode';

var reader = new commonmark.Parser();
var parsed = reader.parse("BBCODE");

var writer = new BBCodeRenderer();
var result = writer.render(parsed);
```

### including the bundle
`<script src="markdown-to-bbcode.js"></script>`

```
var reader = new commonmark.Parser();
var parsed = reader.parse("BBCODE");

var writer = new markdown_to_bbcode.BBCodeRenderer();
var result = writer.render(parsed);
```

### markdown to bbcode webpage:

https://ddormer.github.io/markdown-to-bbcode-site/
