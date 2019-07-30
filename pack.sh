releasename=$1

tar --exclude='./.git' --exclude='./.nant' --exclude='./.vs' -czvf ../${releasename}.tar.gz .
