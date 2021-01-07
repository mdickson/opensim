releasename=$1

tar --exclude='./.git' --exclude='./.nant' --exclude='./.vs' --exclude='./.vscode' --exclude='bin/ScriptEngines'  -czvf ../${releasename}.tar.gz .
