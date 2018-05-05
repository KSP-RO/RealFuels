#! /bin/sh

full_version=$(./tools/git-version-gen --prefix rf .tarball-version s/rf-v/rf/)
file_version=$(echo $full_version | cut -f 1 -d '-')
version=$(echo $file_version | cut -f1-2 -d '.')

sed -e "s/@FULL_VERSION@/$full_version/" -e "s/@FILE_VERSION@/$file_version/" -e "s/@VERSION@/$version/" assembly/AssemblyInfoRF.in > assembly/AssemblyInfoRF.cs-
cmp -s assembly/AssemblyInfoRF.cs assembly/AssemblyInfoRF.cs- || mv assembly/AssemblyInfoRF.cs- assembly/AssemblyInfoRF.cs

rm -f assembly/AssemblyInfoRF.cs-
