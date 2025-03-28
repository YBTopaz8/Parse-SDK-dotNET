# Parse SDK for .NET - MAUI - UNOFFICIAL

A library that gives you access to the powerful Parse Server backend from any platform supporting .NET Standard 2.0 -> .NET 9 / MAUI. For more information about Parse and its features, visit [parseplatform.org](https://parseplatform.org/).

---

## Using the Code
Make sure you are using the project's root namespace.

```csharp
using Parse;
```

The `ParseClient` class has three constructors, one allowing you to specify your Application ID, Server URI, and .NET Key as well as some configuration items, one for creating clones of `ParseClient.Instance` if `Publicize` was called on an instance previously, and another, accepting an `IServerConnectionData` implementation instance which exposes the data needed for the SDK to connect to a Parse Server instance, like the first constructor, but with a few extra options. You can create your own `IServerConnectionData` implementation or use the `ServerConnectionData` struct. `IServerConnectionData` allows you to expose a value the SDK should use as the master key, as well as some extra request headers if needed.

```csharp
ParseClient client = new ParseClient("Your Application ID", "The Parse Server Instance Host URI", "Your .NET Key");
```

```csharp
ParseClient client = new ParseClient(new ServerConnectionData
{
    ApplicationID = "Your Application ID",
    ServerURI = "The Parse Server Instance Host URI",
    Key = "Your .NET Key", // This is unnecessary if a value for MasterKey is specified.
    MasterKey = "Your Master Key",
    Headers = new Dictionary<string, string>
    {
        ["X-Extra-Header"] = "Some Value"
    }
});
```

`ServerConnectionData` is available in the `Parse.Infrastructure` namespace.

The two non-cloning `ParseClient` constructors contain optional parameters for an `IServiceHub` implementation instance and an array of `IServiceHubMutator`s. These should only be used when the behavior of the SDK needs to be changed.

To find full usage instructions for the latest stable release, please visit the [Parse docs website][parse-docs-link]. Please note that the latest stable release is quite old and does not reflect the work being done at the moment.

### Common Definitions

- `Application ID`:
Your app's `ApplicationId` field from your Parse Server.
- `Key`:
Your app's `.NET Key` field from your Parse Server.
- `Master Key`:
Your app's `Master Key` field from your Parse Server. Using this key with the SDK will allow it to bypass any CLPs and object permissions that are set. This also should be compatible with read-only master keys as well.
- `Server URI`:
The full URL to your web-hosted Parse Server.

### Client-Side Use

In your program's entry point, instantiate a `ParseClient` with all the parameters needed to connect to your target Parse Server instance, then call `Publicize` on it. This will light up the static `ParseClient.Instance` property with the newly-instantiated `ParseClient`, so you can perform operations with the SDK.

```csharp
new ParseClient(/* Parameters */).Publicize();
```

### Server-Side Use

The SDK can be set up in a way such that every new `ParseClient` instance can authenticate a different user concurrently. This is enabled by an `IServiceHubMutator` implementation which adds itself as an `IServiceHubCloner` implementation to the service hub which, making it so that consecutive calls to the cloning `ParseClient` constructor (the one without parameters) will clone the publicized `ParseClient` instance, exposed by `ParseClient.Instance`, replacing the `IParseCurrentUserController` implementation instance with a fresh one with no caching every time. This allows you to configure the original instance, and have the clones retain the general behaviour, while also allowing the differnt users to be signed into the their respective clones and execute requests concurrently, without causing race conditions. To use this feature of the SDK, the first `ParseClient` instance must be constructued and publicized as follows once, before any other `ParseClient` instantiations. Any classes that need to be registered must be done so with the original instance.

```csharp
new ParseClient(/* Parameters */, default, new ConcurrentUserServiceHubCloner { }).Publicize();
```

Consecutive instantiations can be done via the cloning constructor for simplicity's sake.

```csharp
ParseClient client = new ParseClient { };
```

### Basic Demonstration

The following code shows how to use the Parse .NET SDK to create a new user, save and authenticate the user, deauthenticate the user, re-authenticate the user, create an object with permissions that allow only the user to modify it, save the object, update the object, delete the object, and deauthenticate the user once more.

```csharp
// Instantiate a ParseClient.
ParseClient client = new ParseClient(/* Parameters */);

// Create a user, save it, and authenticate with it.
await ParseClient.Instance.SignUpWithAsync(username: "Test", password: "Test");

// Get the authenticated user. This is can also be done with a variable that stores the ParseUser instance before the SignUp overload that accepts a ParseUser is called.
Console.WriteLine( await ParseClient.Instance.GetCurrentUser().SessionToken);

// Deauthenticate the user.
await client.LogOutAsync();

// Authenticate the user.
ParseUser? user = await ParseClient.Instance.LogInWithAsync(username: "Test", password: "Test");

// Create a new object with permessions that allow only the user to modify it.
ParseObject testObject = new ParseObject("TestClass") { ACL = new ParseACL(user) };

// Bind the ParseObject to the target ParseClient instance. This is unnecessary if Publicize is called on the client.
testObject.Bind(ParseClient.Instance);

// Set some value on the object.
testObject.Set("someKey", "This is a value.");

// See that the ObjectId of an unsaved object is null;
Console.WriteLine(testObject.ObjectId);

// Save the object to the target Parse Server instance.
await testObject.SaveAsync();

// See that the ObjectId of a saved object is non-null;
Console.WriteLine(testObject.ObjectId);

// Query the object back down from the server to check that it was actually saved.
ParseObject? objectFetched = await ParseClient.Instance.GetQuery("TestClass")
    .WhereEqualTo("objectId", testObject.ObjectId)
    .FirstAsync();
string? someValue = objectFetched.Get<string>("someKey");
Console.WriteLine(someValue);

// Mutate some value on the object.
testObject.Set("someKey", "This is another value.");

// Save the object again.
await testObject.SaveAsync();

// Query the object again to see that the change was made.
ParseObject? objectFetched = await ParseClient.Instance.GetQuery("TestClass")
    .WhereEqualTo("objectId", testObject.ObjectId)
    .FirstAsync();
string? someValue = objectFetched.Get<string>("someKey");
Console.WriteLine(someValue);

// Store the object's objectId so it can be verified that it was deleted later.
var testObjectId = testObject.ObjectId;

// Delete the object.
await testObject.DeleteAsync();

// Check that the object was deleted from the server.
Console.WriteLine();

// Deauthenticate the user again.
await ParseClient.Instance.LogOutAsync();
```

## Local Builds
You can build the SDK on any system with the MSBuild or .NET Core CLI installed. Results can be found under either the `Release/netstandard2.0` or `Debug/netstandard2.0` in the `bin` folder unless a non-standard build configuration is used.

## .NET Core CLI

```batch
dotnet build Parse.sln
```
