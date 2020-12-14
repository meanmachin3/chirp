VERSION=`LANGUAGE=C LC_ALL=C LANG=C perl -n -e '/<Version>([\d.]+)<\/Version>/ && print $1' FsTweet.Web.fsproj`
sed -i -e s/__VERSION__/$VERSION/ "$1"
