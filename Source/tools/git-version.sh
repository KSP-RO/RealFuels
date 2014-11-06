#! /bin/sh

full_version=`./tools/git-version-gen --prefix rf .tarball-version s/rf-v/rf/`
file_version=`echo $full_version | sed -e 's/-/\t/' | cut -f 1`
version=`echo $file_version | sed 's/\./\t/g' | cut -f1-2 | sed -e 's/\t/\./g'`

sed -e "s/@FULL_VERSION@/$full_version/" -e "s/@FILE_VERSION@/$file_version/" -e "s/@VERSION@/$version/" assembly/AssemblyInfoRF.in > assembly/AssemblyInfoRF.cs-
cmp -s assembly/AssemblyInfoRF.cs assembly/AssemblyInfoRF.cs- || mv assembly/AssemblyInfoRF.cs- assembly/AssemblyInfoRF.cs

full_version=`./tools/git-version-gen --prefix mft .tarball-version s/mft-/mft/`
version=`echo $full_version | sed -e 's/-/\t/' | cut -f 1`

sed -e "s/@FULL_VERSION@/$full_version/" -e "s/@VERSION@/$version/" assembly/AssemblyInfoMFT.in > assembly/AssemblyInfoMFT.cs-
cmp -s assembly/AssemblyInfoMFT.cs assembly/AssemblyInfoMFT.cs- || mv assembly/AssemblyInfoMFT.cs- assembly/AssemblyInfoMFT.cs

rm -f assembly/*.cs-
