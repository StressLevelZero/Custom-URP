#printf "$@"
git show -s --format=%H > prevGitCommitHash.txt
git add ./prevGitCommitHash.txt
./../../Tools/IncrementVersionNumber/IncrementVersionNumber.exe package.json
git add ./package.json
git commit -m "$@"
git archive --format=tar.gz -o ./../../RenderingPackageArchives/com.unity.render-pipelines.universal.tar.gz HEAD