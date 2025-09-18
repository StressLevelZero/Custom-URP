PACKAGE_NAME=com.unity.render-pipelines.universal

if [[ $# == 0 ]]; then
    echo "USAGE: ./CommitAndArchive.sh [OPTIONS] \"Commit message\""
    echo "Increments the package version number, dumps the current commit hash to a "
    echo "file, commits the pending changes, and archives the project to a tarball"
    echo "suitable for use with Unity Package Manager as a \"local-tarball\" type "
    echo "package"
    echo "Options:"
    echo "    -i     Skip incrementing the version number"
    echo "    -h     Skip dumping the commit hash to prevGitCommitHash.txt"
    echo "    -c     Skip commiting the changes"
    echo "    -a     Skip archiving the package to a tarball"
    echo "    -p     Push the commit"
    exit
fi

while getopts 'ihcap' opt; do
    case $opt in
        i) 
        NO_INCREMENT="SET"
        ;;
        h) NO_HASH="SET"
        ;;
        c) NO_COMMIT="SET"
        ;;
        a) NO_ARCHIVE="SET"
        ;;
        p) PUSH_COMMIT="SET"
        ;;
    esac
done
shift $((OPTIND-1))
COMMIT_MESSAGE="$@"

if [[ ! -v NO_COMMIT ]] && [[ -z "${COMMIT_MESSAGE}" ]]; then
    "Missing commit message"
    exit
fi

# Dump current commit hash to a file
if [[ ! -v NO_HASH ]]; then
    git show -s --format=%H > prevGitCommitHash.txt
    git add ./prevGitCommitHash.txt
fi

# Run a C# script in the RenderPipelines project to increment the package.json minor version number
if [[ ! -v NO_INCREMENT ]]; then
    ./../../Tools/IncrementVersionNumber/IncrementVersionNumber.exe package.json
    git add ./package.json
fi

# Commit the pending changes with the arguments to this script as the commit message
if [[ ! -v NO_COMMIT ]]; then
    git commit -m "\"$COMMIT_MESSAGE\""
fi

if [[ ! -v NO_ARCHIVE ]]; then
    git archive --format=tar.gz --prefix="package/" -o ./../../RenderingPackageArchives/$PACKAGE_NAME.tar.gz HEAD
fi

if [[ -v PUSH_COMMIT ]]; then
    git push
fi