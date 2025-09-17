git show -s --format=%H > prevGitCommitHash.txt
git add ./PreviousGitCommitHash.txt
git commit -m $1
git archive --format=tar.gz -o ./../com.unity.render-pipelines.universal.tar.gz HEAD
git push