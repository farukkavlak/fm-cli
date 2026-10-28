#!/bin/zsh
# Publishes a router model as a release of this repository, where `fm doctor --install`
# downloads it from (cli/fm/Nl/ModelDownload.cs: NlModelName, NlModelFiles, NlModelUrl).
# The repository has to be public for others to download it.
#   tools/nl-release.sh                  # the model ModelDownload.cs names, from ~/.cache/fm-cli/nl/<name>
set -eu
cd "${0:A:h}/.."
NAME=$(sed -n 's/.*NlModelName = "\(.*\)";/\1/p' cli/fm/Nl/ModelDownload.cs)
DIR="$HOME/.cache/fm-cli/nl/$NAME"
for f in model.onnx tokenizer.json; do
	[ -f "$DIR/$f" ] || { echo "missing: $DIR/$f"; exit 1; }
	sum=$(shasum -a 256 "$DIR/$f" | cut -d' ' -f1)
	grep -q "$sum" cli/fm/Nl/ModelDownload.cs || { echo "$f: its SHA-256 ($sum) is not in ModelDownload.cs"; exit 1; }
done
gh release create "nl-$NAME" "$DIR/model.onnx" "$DIR/tokenizer.json" \
	--title "Language model $NAME" \
	--notes "The router model for sentences in place of commands, downloaded by \`fm doctor --install\`. Fine-tuned from sentence-transformers/paraphrase-multilingual-mpnet-base-v2 (Apache-2.0), int8."
