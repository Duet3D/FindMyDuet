# FindMyDuet

An Android app to discover Duet boards via mDNS and to open their web interface in an embedded browser.

## Building

The app targets .NET 10 for Android. To set up a build environment, install the .NET 10 SDK, add the Android workload and let it provision the Android SDK:

```
dotnet workload install android
dotnet build FindMyDuet/FindMyDuet.csproj -t:InstallAndroidDependencies -p:AcceptAndroidSDKLicenses=True
```

A JDK 17 or 21 is required as well. Pass `-p:JavaSdkDirectory=<path>` if it is not picked up automatically.

Release builds for Google Play are app bundles and have to be signed with the release keystore:

```
dotnet publish FindMyDuet/FindMyDuet.csproj -c Release -p:AndroidPackageFormat=aab \
	-p:AndroidKeyStore=true -p:AndroidSigningKeyStore=<keystore> -p:AndroidSigningKeyAlias=<alias> \
	-p:AndroidSigningStorePass=<password> -p:AndroidSigningKeyPass=<password>
```

## Bug reports

Please use the [forum](https://forum.duet3d.com) for support requests.
