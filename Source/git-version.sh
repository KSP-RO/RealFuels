#! /bin/sh

full_version=`./git-version-gen --prefix rf .tarball-version s/rf-v/rf/`
version=`echo $full_version | sed -e 's/-/\t/' | cut -f 1`

sed -e "s/@FULL_VERSION@/$full_version/" -e "s/@VERSION@/$version/" AssemblyInfoRF.in > AssemblyInfoRF.cs-
cmp -s AssemblyInfoRF.cs AssemblyInfoRF.cs- || mv AssemblyInfoRF.cs- AssemblyInfoRF.cs

full_version=`./git-version-gen --prefix mft .tarball-version s/mft-/mft/`
version=`echo $full_version | sed -e 's/-/\t/' | cut -f 1`

sed -e "s/@FULL_VERSION@/$full_version/" -e "s/@VERSION@/$version/" AssemblyInfoMFT.in > AssemblyInfoMFT.cs-
cmp -s AssemblyInfoMFT.cs AssemblyInfoMFT.cs- || mv AssemblyInfoMFT.cs- AssemblyInfoMFT.cs

rm -f *.cs-
