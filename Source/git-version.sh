#! /bin/sh

full_version=`./git-version-gen --prefix mft .tarball-version s/mft-/mft/`
version=`echo $full_version | sed -e 's/-/\t/' | cut -f 1`

sed -e "s/@FULL_VERSION@/$full_version/" -e "s/@VERSION@/$version/" AssemblyInfo.in > AssemblyInfo.cs-

cmp -s AssemblyInfo.cs AssemblyInfo.cs- || mv AssemblyInfo.cs- AssemblyInfo.cs

rm -f *.cs-
