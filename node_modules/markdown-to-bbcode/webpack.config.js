const path = require('path');

module.exports = [
  {
  entry: './src/index.js',
  target: 'web',
  output: {
    filename: 'markdown-to-bbcode.js',
    path: path.resolve(__dirname, 'dist'),
    library: {
      name: 'markdown_to_bbcode',
      type: 'umd',
    }
  }
  },
  {
  entry: './src/index.js',
  target: 'node',
  output: {
    filename: 'markdown-to-bbcode.node.js',
    path: path.resolve(__dirname, 'dist'),
    library: {
      type: 'umd',
    }
  },
}
];
