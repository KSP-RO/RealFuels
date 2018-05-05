#! /bin/sh

full_version=$(./tools/git-version-gen --prefix mft .tarball-version s/mft-/mft/)
version=$(echo $full_version | cut -f 1 -d '-')

sed -e "s/@FULL_VERSION@/$full_version/" -e "s/@VERSION@/$version/" assembly/AssemblyInfoMFT.in > assembly/AssemblyInfoMFT.cs-
cmp -s assembly/AssemblyInfoMFT.cs assembly/AssemblyInfoMFT.cs- || mv assembly/AssemblyInfoMFT.cs- assembly/AssemblyInfoMFT.cs

rm -f assembly/AssemblyInfoMFT.cs-
