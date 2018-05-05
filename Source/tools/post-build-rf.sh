if [ -z "${TARGET_PATH}" ] ; then
  echo 'Expected $TARGET_PATH to be defined but it is not' >&2
  exit 1
elif ! [ -f "${TARGET_PATH}" ] ; then
  echo 'Expected $TARGET_PATH to be a file but it is not' >&2
  exit 1
fi

if [ -z "${TARGET_DIR}" ] ; then
  echo 'Expected $TARGET_DIR to be defined but it is not' >&2
  exit 1
elif ! [ -d "${TARGET_DIR}" ] ; then
  echo 'Expected $TARGET_DIR to be a directory but it is not' >&2
  exit 1
fi

rm -vf "${TARGET_DIR}/System.Core.dll"

if [ -z "${PDB2MDB}" ] ; then
  echo '$PDB2MDB not found'
else
  echo "Running '${PDB2MDB}' on '${TARGET_PATH}'"
  "${PDB2MDB}" "${TARGET_PATH}"
fi
