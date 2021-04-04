import { escape } from 'html-escaper';
import { Renderer } from 'commonmark';

var reUnsafeProtocol = /^javascript:|vbscript:|file:|data:/i;
var reSafeDataProtocol = /^data:image\/(?:png|gif|jpeg|webp)/i;

var potentiallyUnsafe = function(url) {
    return reUnsafeProtocol.test(url) && !reSafeDataProtocol.test(url);
};

// Helper function to produce a BBCode tag.
function tag(name, attrs, selfclosing) {
    console.log(attrs);
    if (this.disableTags > 0) {
        return;
    }
    this.buffer += "[" + name;
    if (attrs && attrs.length > 0) {
        var i = 0;
        var attrib;
        while ((attrib = attrs[i]) !== undefined) {
            if (attrib[0] == "tag_value") {
                this.buffer += '=' + attrib[1];
            }
            else {
                this.buffer += " " + attrib[0] + '="' + attrib[1] + '"';
            }
            i++;
        }
    }
    if (selfclosing) {
        this.buffer += " /";
    }
    this.buffer += "]";
    this.lastOut = "]";
}

function BBCodeRenderer(options) {
    options = options || {};
    options.softbreak = options.softbreak || "\n";
    options.newline_after_heading = options.newline_after_heading || true;
    this.esc = options.esc || escape;
    this.disableTags = 0;
    this.lastOut = "\n";
    this.options = options;
}

/* Node methods */

function text(node) {
    this.out(node.literal);
}

function softbreak() {
    this.lit(this.options.softbreak);
}

function linebreak() {
    this.cr();
}

function link(node, entering) {
    var attrs = this.attrs(node);
    if (entering) {
        if (!(this.options.safe && potentiallyUnsafe(node.destination))) {
            this.tag("url=" + this.esc(node.destination));
        }
        if (node.title) {
            this.text(node.title);
        }
    } else {
        this.tag("/url");
    }
}

function image(node, entering) {
    if (entering) {
        if (this.disableTags === 0) {
            if (this.options.safe && potentiallyUnsafe(node.destination)) {
                this.lit('[img]');
            } else {
                this.lit('[img]' + this.esc(node.destination));
            }
        }
        this.disableTags += 1;
    } else {
        this.disableTags -= 1;
        if (this.disableTags === 0) {
            if (node.title) {
                this.lit('" title="' + this.esc(node.title));
            }
            this.lit('[/img]');
        }
    }
}

function emph(node, entering) {
    this.tag(entering ? "i" : "/i");
}

function strong(node, entering) {
    this.tag(entering ? "b" : "/b");
}

function paragraph(node, entering) {
    var grandparent = node.parent.parent,
        attrs = this.attrs(node);
    if (grandparent !== null && grandparent.type === "list") {
        if (grandparent.listTight) {
            return;
        }
    }
    if (entering) {
        this.lit(this.options.softbreak);
    } else {
        this.cr();
        this.lit(this.options.softbreak);
    }
}

function heading(node, entering) {
    var starting_size = 8;
    if (entering) {
        this.cr();
        this.tag('b');
        this.tag("size=" + (starting_size - node.level));
    } else {
        this.tag("/size");
        this.tag('/b');
        if (this.options.newline_after_heading) {
            this.lit(this.options.softbreak);
        }
    }
}

function code(node) {
    this.tag("code");
    this.out(node.literal);
    this.tag("/code");
}

function code_block(node) {
    var info_words = node.info ? node.info.split(/\s+/) : [],
        attrs = this.attrs(node);
    if (info_words.length > 0 && info_words[0].length > 0) {
        attrs.push(["tag_value", this.esc(info_words[0])]);
    }
    this.cr();
    this.tag("pre");
    this.tag("code", attrs);
    this.out(node.literal);
    this.tag("/code");
    this.tag("/pre");
    this.cr();
}

function thematic_break(node) {
    var attrs = this.attrs(node);
    this.cr();
    this.lit(this.options.softbreak);
}

function block_quote(node, entering) {
    var attrs = this.attrs(node);
    if (entering) {
        this.cr();
        this.tag("quote", attrs);
        this.cr();
    } else {
        this.cr();
        this.tag("/quote");
        this.cr();
    }
}

function list(node, entering) {
    var attrs = this.attrs(node);
    if (node.listType === "ordered") {
        attrs.push(["tag_value", 1]);
    }
    if (entering) {
        var start = node.listStart;
        if (start !== null && start !== 1) {
            attrs.push(["start", start.toString()]);
        }
        this.cr();
        this.tag("list", attrs);
        this.cr();
    } else {
        this.cr();
        this.tag("/list");
        this.cr();
    }
}

function item(node, entering) {
    var attrs = this.attrs(node);
    if (entering) {
        this.tag("*", attrs);
    } else {
        this.cr();
    }
}

function html_inline(node) {
    if (this.options.safe) {
        this.lit("<!-- raw HTML omitted -->");
    } else {
        this.lit(node.literal);
    }
}

function html_block(node) {
    this.cr();
    if (this.options.safe) {
        this.lit("<!-- raw HTML omitted -->");
    } else {
        this.lit(node.literal);
    }
    this.cr();
}

function custom_inline(node, entering) {
    if (entering && node.onEnter) {
        this.lit(node.onEnter);
    } else if (!entering && node.onExit) {
        this.lit(node.onExit);
    }
}

function custom_block(node, entering) {
    this.cr();
    if (entering && node.onEnter) {
        this.lit(node.onEnter);
    } else if (!entering && node.onExit) {
        this.lit(node.onExit);
    }
    this.cr();
}

/* Helper methods */

function out(s) {
    this.lit(this.esc(s));
}

function attrs(node) {
    var att = [];
    if (this.options.sourcepos) {
        var pos = node.sourcepos;
        if (pos) {
            att.push([
                "data-sourcepos",
                String(pos[0][0]) +
                    ":" +
                    String(pos[0][1]) +
                    "-" +
                    String(pos[1][0]) +
                    ":" +
                    String(pos[1][1])
            ]);
        }
    }
    return att;
}

// quick browser-compatible inheritance
BBCodeRenderer.prototype = Object.create(Renderer.prototype);
BBCodeRenderer.prototype.text = text;
BBCodeRenderer.prototype.html_inline = html_inline;
BBCodeRenderer.prototype.html_block = html_block;
BBCodeRenderer.prototype.softbreak = softbreak;
BBCodeRenderer.prototype.linebreak = linebreak;
BBCodeRenderer.prototype.link = link;
BBCodeRenderer.prototype.image = image;
BBCodeRenderer.prototype.emph = emph;
BBCodeRenderer.prototype.strong = strong;
BBCodeRenderer.prototype.paragraph = paragraph;
BBCodeRenderer.prototype.heading = heading;
BBCodeRenderer.prototype.code = code;
BBCodeRenderer.prototype.code_block = code_block;
BBCodeRenderer.prototype.thematic_break = thematic_break;
BBCodeRenderer.prototype.block_quote = block_quote;
BBCodeRenderer.prototype.list = list;
BBCodeRenderer.prototype.item = item;
BBCodeRenderer.prototype.custom_inline = custom_inline;
BBCodeRenderer.prototype.custom_block = custom_block;
BBCodeRenderer.prototype.esc = escape;
BBCodeRenderer.prototype.out = out;
BBCodeRenderer.prototype.tag = tag;
BBCodeRenderer.prototype.attrs = attrs;

export { BBCodeRenderer };
