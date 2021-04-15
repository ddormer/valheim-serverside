#!/usr/bin/env bash

git config --local user.email "$(git log --format='%ae' HEAD^!)"
git config --local user.name "$(git log --format='%an' HEAD^!)"

git remote add github "https://$GITHUB_ACTOR:$GITHUB_TOKEN@github.com/$GITHUB_REPOSITORY.git"
git pull github ${GITHUB_REF} --ff-only

npm install markdown-to-bbcode
node --experimental-modules bin/build-bbcode.mjs

git diff-files README.bbcode

if ! `git diff-files --quiet README.bbcode` ; then
  echo "No README.bbcode changes found, exiting."
  exit 0
fi

git add README.bbcode
git commit -m "Update README.bbcode"
git push github HEAD:${GITHUB_REF}
