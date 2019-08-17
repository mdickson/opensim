releasename=$1

tar --exclude='./.git' --exclude='./.nant' --exclude='./.vs' --exclude='bin/ScriptEngines'  -czvf ../${releasename}.tar.gz .
