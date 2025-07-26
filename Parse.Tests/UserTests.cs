using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Control;
using Parse.Abstractions.Infrastructure.Execution;
using Parse.Abstractions.Platform.Objects;
using Parse.Abstractions.Platform.Sessions;
using Parse.Abstractions.Platform.Users;
using Parse.Infrastructure;
using Parse.Infrastructure.Execution;
using Parse.Platform.Objects;

namespace Parse.Tests;

[TestClass]
public class UserTests
{
    private const string TestSessionToken = "llaKcolnu";
    private const string TestRevocableSessionToken = "r:llaKcolnu";
    private const string TestObjectId = "some0neTol4v4";
    private const string TestUsername = "ihave";
    private const string TestPassword = "adream";
    private const string TestEmail = "gogo@parse.com";

    private ParseClient Client { get; set; }

    [TestInitialize]
    public void SetUp()
    {
        
        Client = new ParseClient(new ServerConnectionData { Test = true });
        Client.Publicize();  // Ensure the Clientinstance is globally available

        
        Client.AddValidClass<ParseSession>();
        Client.AddValidClass<ParseUser>();

        // Ensure TLS 1.2 (or appropriate) is enabled if needed
        System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

    }
    [TestCleanup]
    public void CleanUp()
    {
        (Client.Services as ServiceHub)?.Reset();
    }

    /// <summary>
    /// Factory method for creating ParseUser objects with the ServiceHub bound.
    /// </summary>
    private ParseUser CreateParseUser(MutableObjectState state)
    {
        var user = ParseObject.Create<ParseUser>();
        
        user.HandleFetchResult(state);
        user.Bind(Client);
        

        return user;
    }

    [TestMethod]
    public async Task TestSignUpWithInvalidServerDataAsync()
    {
        var state = new MutableObjectState
        {
            ServerData = new Dictionary<string, object>
            {
                ["sessionToken"] = TestSessionToken
            }
        };

        var user = CreateParseUser(state);

        // Simulate invalid server data by ensuring username and password are not set
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            async () => await user.SignUpAsync(),
            "Expected SignUpAsync to throw an exception due to missing username or password."
        );
    }


    [TestMethod]
    public async Task TestSignUpAsync()
    {
        var state = new MutableObjectState
        {
            ServerData = new Dictionary<string, object>
            {
                ["sessionToken"] = TestSessionToken,
                ["username"] = TestUsername,
                ["password"] = TestPassword
            }
        };

        var newState = new MutableObjectState
        {
            ObjectId = TestObjectId
        };

        var hub = new MutableServiceHub();
        var client = new ParseClient(new ServerConnectionData { Test = true }, hub);

        var mockController = new Mock<IParseUserController>();
        mockController
            .Setup(obj => obj.SignUpAsync(It.IsAny<IObjectState>(), It.IsAny<IDictionary<string, IParseFieldOperation>>(), It.IsAny<IServiceHub>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newState);

        hub.UserController = mockController.Object;

        var user = CreateParseUser(state);
        user.Bind(client);

        
        await user.SignUpAsync();
        

        // Verify SignUpAsync is invoked
        mockController.Verify(
            obj => obj.SignUpAsync(
                It.IsAny<IObjectState>(),
                It.IsAny<IDictionary<string, IParseFieldOperation>>(),
                It.IsAny<IServiceHub>(),
                It.IsAny<CancellationToken>()),
            Times.Once
        );

        Assert.IsFalse(user.IsDirty);
        Assert.AreEqual(TestUsername, user.Username);
        Assert.IsFalse(user.State.ContainsKey("password"));
        Assert.AreEqual(TestObjectId, user.ObjectId);
    }


    [TestMethod]
    public async Task TestLogOut()
    {
        // Arrange

        // 1. Create mocks for the specific services we need to control.
        var mockCommandRunner = new Mock<IParseCommandRunner>();
        var mockCurrentUserController = new Mock<IParseCurrentUserController>();

        // 2. Create a MUTABLE service hub and put our mocks inside.
        //    This hub has NO knowledge of the real services.
        var mockedHub = new MutableServiceHub
        {
            CommandRunner = mockCommandRunner.Object,
            CurrentUserController = mockCurrentUserController.Object
        };
        // Let the mutable hub fill in any other dependencies it needs with defaults.
        mockedHub.SetDefaults();

        // 3. Create a NEW ParseClient instance specifically for this test.
        //    We pass our MOCKED hub directly into its constructor.
        var isolatedClient = new ParseClient(new ServerConnectionData { Test = true }, mockedHub);

        // 4. Use THIS isolated client to create our user.
        //    This guarantees the user is constructed ONLY with our mocked services.
        //    It will never touch the static ParseClient.Instance.
        var user = isolatedClient.GenerateObjectFromState<ParseUser>(new MutableObjectState
        {
            ServerData = new Dictionary<string, object> { ["sessionToken"] = TestRevocableSessionToken }
        }, "_User");

        // 5. Set up the expected behavior of our mocks.
        mockCommandRunner.Setup(runner => runner.RunCommandAsync(
                It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, new Dictionary<string, object>()));

        mockCurrentUserController
            .Setup(c => c.LogOutAsync(It.IsAny<IServiceHub>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        // Call LogOutAsync on the user object that is guaranteed to be isolated.
        await user.LogOutAsync(CancellationToken.None);
        mockCommandRunner.Verify(runner => runner.RunCommandAsync(
    It.Is<ParseCommand>(cmd =>
        // Check the path
        cmd.Path.Contains("logout") &&
        // Manually check the headers
        HeadersContainSessionToken(cmd.Headers, TestRevocableSessionToken)
    ),
            It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify the local cache was told to clear.
        mockCurrentUserController.Verify(c =>
            c.LogOutAsync(It.IsAny<IServiceHub>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.IsNull(user.SessionToken);
    }



    private bool HeadersContainSessionToken(IEnumerable<KeyValuePair<string, string>> headers, string expectedToken)
    {
        foreach (var header in headers)
        {
            if (header.Key == "X-Parse-Session-Token" && header.Value == expectedToken)
            {
                return true; // We found it!
            }
        }
        return false; // We looped through all headers and didn't find it.
    }
    [TestMethod]
    public async Task TestRequestPasswordResetAsync()
    {
        var hub = new MutableServiceHub();
        var Client= new ParseClient(new ServerConnectionData { Test = true }, hub);

        var mockController = new Mock<IParseUserController>();
        hub.UserController = mockController.Object;

        await Client.RequestPasswordResetAsync(TestEmail);

        mockController.Verify(obj => obj.RequestPasswordResetAsync(TestEmail, It.IsAny<CancellationToken>()), Times.Once);
    }


    //I need to test the LinkWithAsync method, but it requires a valid authData dictionary and a valid service hub setup.
    [Ignore]
    public async Task TestLinkAsync()
    {
        // Arrange
        var state = new MutableObjectState
        {
            ObjectId = TestObjectId,
            ServerData = new Dictionary<string, object>
            {
                ["sessionToken"] = TestSessionToken
            }
        };

        var hub = new MutableServiceHub();
        var client = new ParseClient(new ServerConnectionData { Test = true }, hub);

        var user = CreateParseUser(state);

        var mockObjectController = new Mock<IParseObjectController>();

        // Update: Remove the ThrowsAsync to allow SaveAsync to execute without throwing
        mockObjectController
            .Setup(obj => obj.SaveAsync(
                It.IsAny<IObjectState>(),
                It.IsAny<IDictionary<string, IParseFieldOperation>>(),
                It.IsAny<string>(),
                It.IsAny<IServiceHub>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Mock<IObjectState>().Object) // Provide a mock IObjectState
            .Verifiable();

        hub.ObjectController = mockObjectController.Object;

        var authData = new Dictionary<string, object>
    {
        { "id", "testUserId" },
        { "access_token", "12345" }
    };

        // Act
        try
        {
            await user.LinkWithAsync("parse", authData, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Check if the exception is expected and pass the test if it matches
            Assert.AreEqual("Page does not exist", ex.Message, "Unexpected exception message.");
        }
        // Additional assertions to ensure the user state is as expected after linking
        Assert.IsTrue(user.IsDirty, "User should be marked as dirty after unsuccessful save.");
        Assert.IsNotNull(user.AuthData);
        Assert.IsNotNull(user.AuthData);
        Assert.AreEqual(TestObjectId, user.ObjectId);
    }

    [TestMethod]
    public async Task TestUserSave()
    {
        IObjectState state = new MutableObjectState
        {
            ObjectId = "some0neTol4v4",
            ServerData = new Dictionary<string, object>
            {
                ["sessionToken"] = "llaKcolnu",
                ["username"] = "ihave",
                ["password"] = "adream"
            }
        };

        IObjectState newState = new MutableObjectState
        {
            ServerData = new Dictionary<string, object>
            {
                ["Alliance"] = "rekt"
            }
        };

        var hub = new MutableServiceHub();
        var client = new ParseClient(new ServerConnectionData { Test = true }, hub);

        var user = client.GenerateObjectFromState<ParseUser>(state, "_User");

        var mockObjectController = new Mock<IParseObjectController>();
        mockObjectController.Setup(obj => obj.SaveAsync(
            It.IsAny<IObjectState>(),
            It.IsAny<IDictionary<string, IParseFieldOperation>>(),
            It.IsAny<string>(),
            It.IsAny<IServiceHub>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(newState);

        hub.ObjectController = mockObjectController.Object;
        hub.CurrentUserController = new Mock<IParseCurrentUserController>().Object;

        user["Alliance"] = "rekt";

        // Await the save operation instead of using ContinueWith
        await user.SaveAsync();

        // Assertions after await
        mockObjectController.Verify(obj => obj.SaveAsync(
            It.IsAny<IObjectState>(),
            It.IsAny<IDictionary<string, IParseFieldOperation>>(),
            It.IsAny<string>(),
            It.IsAny<IServiceHub>(),
            It.IsAny<CancellationToken>()), Times.Exactly(1));

        Assert.IsFalse(user.IsDirty);
        Assert.AreEqual("ihave", user.Username);
        Assert.IsFalse(user.State.ContainsKey("password"));
        Assert.AreEqual("some0neTol4v4", user.ObjectId);
        Assert.AreEqual("rekt", user["Alliance"]);
    }
    [TestMethod]
    public async Task TestSaveAsync_IsCalled()
    {
        // Arrange
        var mockObjectController = new Mock<IParseObjectController>();
        mockObjectController
            .Setup(obj => obj.SaveAsync(
                It.IsAny<IObjectState>(),
                It.IsAny<IDictionary<string, IParseFieldOperation>>(),
                It.IsAny<string>(),
                It.IsAny<IServiceHub>(),
                It.IsAny<CancellationToken>()))
            
            .Verifiable();

        // Act
        await mockObjectController.Object.SaveAsync(null, null, null, null, CancellationToken.None);

        // Assert
        mockObjectController.Verify(obj =>
            obj.SaveAsync(
                It.IsAny<IObjectState>(),
                It.IsAny<IDictionary<string, IParseFieldOperation>>(),
                It.IsAny<string>(),
                It.IsAny<IServiceHub>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

}
