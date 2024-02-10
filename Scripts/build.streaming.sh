#!/bin/sh

# ./Scripts/build.streaming.sh BUILD="1-beta1" VERSION="6.0.0" IOSVERSION="6.0.0" ANDROIDVERSION="6.0.0" MACVERSION="6.0.0"
for ARGUMENT in "$@"
do
   KEY=$(echo $ARGUMENT | cut -f1 -d=)

   KEY_LENGTH=${#KEY}
   VALUE="${ARGUMENT:$KEY_LENGTH+1}"

   export "$KEY"="$VALUE"
done

if [ -z "$VERSION" ]
then
echo "No target Argument for nuget version"
else
echo "$IOSVERSION" > Bindings/Red5Pro.Streaming.Net.iOS/Readme.md
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$IOSVERSION.7/" Bindings/Red5Pro.Streaming.Net.iOS/Red5Pro.Streaming.Net.iOS.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-ios/<TargetFramework>net7.0-ios/" Bindings/Red5Pro.Streaming.Net.iOS/Red5Pro.Streaming.Net.iOS.csproj
dotnet pack Bindings/Red5Pro.Streaming.Net.iOS/Red5Pro.Streaming.Net.iOS.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$IOSVERSION.8/" Bindings/Red5Pro.Streaming.Net.iOS/Red5Pro.Streaming.Net.iOS.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-ios/<TargetFramework>net8.0-ios/" Bindings/Red5Pro.Streaming.Net.iOS/Red5Pro.Streaming.Net.iOS.csproj
dotnet pack Bindings/Red5Pro.Streaming.Net.iOS/Red5Pro.Streaming.Net.iOS.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
cd NugetPackages
rm -rf streamingios
unzip -n -q Red5Pro.Streaming.Net.iOS.$IOSVERSION.7.nupkg -d streamingios
unzip -n -q Red5Pro.Streaming.Net.iOS.$IOSVERSION.8.nupkg -d streamingios
rm Red5Pro.Streaming.Net.iOS.$IOSVERSION.7.nupkg
rm Red5Pro.Streaming.Net.iOS.$IOSVERSION.8.nupkg
cd ..
echo "ios part nugets generated"
echo "$MACVERSION" > Bindings/Red5Pro.Streaming.Net.Mac/Readme.md
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$MACVERSION.7/" Bindings/Red5Pro.Streaming.Net.Mac/Red5Pro.Streaming.Net.Mac.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-maccatalyst/<TargetFramework>net7.0-maccatalyst/" Bindings/Red5Pro.Streaming.Net.Mac/Red5Pro.Streaming.Net.Mac.csproj
dotnet pack Bindings/Red5Pro.Streaming.Net.Mac/Red5Pro.Streaming.Net.Mac.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$MACVERSION.8/" Bindings/Red5Pro.Streaming.Net.Mac/Red5Pro.Streaming.Net.Mac.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-maccatalyst/<TargetFramework>net8.0-maccatalyst/" Bindings/Red5Pro.Streaming.Net.Mac/Red5Pro.Streaming.Net.Mac.csproj
dotnet pack Bindings/Red5Pro.Streaming.Net.Mac/Red5Pro.Streaming.Net.Mac.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
cd NugetPackages
rm -rf streamingmac
unzip -n -q Red5Pro.Streaming.Net.Mac.$MACVERSION.7.nupkg -d streamingmac
unzip -n -q Red5Pro.Streaming.Net.Mac.$MACVERSION.8.nupkg -d streamingmac
rm Red5Pro.Streaming.Net.Mac.$MACVERSION.7.nupkg
rm Red5Pro.Streaming.Net.Mac.$MACVERSION.8.nupkg
cd ..
echo "mac part nugets generated"
echo "$ANDROIDVERSION" > Bindings/Red5Pro.Streaming.Net.Android/Readme.md
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$ANDROIDVERSION.7/" Bindings/Red5Pro.Streaming.Net.Android/Red5Pro.Streaming.Net.Android.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-android/<TargetFramework>net7.0-android/" Bindings/Red5Pro.Streaming.Net.Android/Red5Pro.Streaming.Net.Android.csproj
dotnet pack Bindings/Red5Pro.Streaming.Net.Android/Red5Pro.Streaming.Net.Android.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
sed -E -i "" "s/<Version>([0-9]{1,}\.)+[0-9]{1,}/<Version>$ANDROIDVERSION.8/" Bindings/Red5Pro.Streaming.Net.Android/Red5Pro.Streaming.Net.Android.csproj
sed -E -i "" "s/<TargetFramework>net([0-9]{1,}\.)+[0-9]{1,}-android/<TargetFramework>net8.0-android/" Bindings/Red5Pro.Streaming.Net.Android/Red5Pro.Streaming.Net.Android.csproj
dotnet pack Bindings/Red5Pro.Streaming.Net.Android/Red5Pro.Streaming.Net.Android.csproj --output NugetPackages --force --verbosity quiet --property WarningLevel=0 /clp:ErrorsOnly
cd NugetPackages
rm -rf streamingandroid
unzip -n -q Red5Pro.Streaming.Net.Android.$ANDROIDVERSION.7.nupkg -d streamingandroid
unzip -n -q Red5Pro.Streaming.Net.Android.$ANDROIDVERSION.8.nupkg -d streamingandroid
rm Red5Pro.Streaming.Net.Android.$ANDROIDVERSION.7.nupkg
rm Red5Pro.Streaming.Net.Android.$ANDROIDVERSION.8.nupkg
cd ..
echo "android part nugets generated"
cd NugetPackages

cp -R streamingandroid/Readme.md streamingandroid/Readme.txt
cp -R streamingmac/Readme.md streamingmac/Readme.txt
cp -R streamingios/Readme.md streamingios/Readme.txt
cp -R streaming/Readme.md streaming/Readme.txt

# mkdir Voice/native
# mkdir Voice/native/lib
# mkdir Voice/native/lib/ios
# cp -R webrtc/lib/net8.0-android34.0/webrtc.aar webrtc/native/lib/android
# 
# rm webrtc/lib/net8.0-android34.0/webrtc.aar
# rm webrtc/lib/net7.0-android33.0/webrtc.aar 


sed -E -i "" "s/<version>([0-9]{1,}\.)+[0-9]{1,}/<version>$ANDROIDVERSION.$BUILD/" Red5Pro.Streaming.Net.Android.nuspec
sed -E -i "" "s/<version>([0-9]{1,}\.)+[0-9]{1,}/<version>$IOSVERSION.$BUILD/" Red5Pro.Streaming.Net.iOS.nuspec
sed -E -i "" "s/<version>([0-9]{1,}\.)+[0-9]{1,}/<version>$MACVERSION.$BUILD/" Red5Pro.Streaming.Net.Mac.nuspec
sed -E -i "" "s/<version>([0-9]{1,}\.)+[0-9]{1,}/<version>$VERSION.$BUILD/" Red5Pro.Streaming.Net.nuspec

nuget pack Red5Pro.Streaming.Net.Android.nuspec
nuget pack Red5Pro.Streaming.Net.iOS.nuspec
nuget pack Red5Pro.Streaming.Net.Mac.nuspec
nuget pack Red5Pro.Streaming.Net.nuspec

rm -rf streamingandroid
rm -rf streamingios
rm -rf streamingmac

# if  [ -z "$3" ]
# then
# echo "package ready"
# unzip -n -q OpenTok.Net.webrtc.Dependency.Android.$VERSION.$2.nupkg -d webrtc
# else 
# dotnet nuget push OpenTok.Net.webrtc.Dependency.Android.$VERSION.$2.nupkg --api-key $3 --source https://api.nuget.org/v3/index.json --timeout 3000000
# # rm OpenTok.Net.webrtc.Dependency.Android.$VERSION.$2.nupkg
# fi
# cd ..
fi