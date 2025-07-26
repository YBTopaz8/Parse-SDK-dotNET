using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Parse.Abstractions.Internal;
using Moq;

using Parse.Abstractions.Infrastructure;
using Parse.Abstractions.Infrastructure.Data;
using Parse.Abstractions.Infrastructure.Execution;
using Parse.Abstractions.Platform.Objects;
using Parse.Infrastructure;
using Parse.Infrastructure.Execution;
using Parse.Platform.Objects;
using Parse.Platform.Queries;

namespace Parse.Tests;

[TestClass]
public class ParseQueryControllerTests
{
    private Mock<IParseCommandRunner> mockCommandRunner;
    private Mock<IParseDataDecoder> mockDecoder;
    private ParseClient client;
    [TestMethod]
    [Description("Tests that FindAsync correctly decodes a list of objects from the server response.")]
    public async Task FindAsync_WithResults_ReturnsDecodedStates()
    {
        // Arrange
        var controller = new ParseQueryController(mockCommandRunner.Object, mockDecoder.Object);
        var query = new ParseQuery<ParseObject>(client.Services, "TestClass");
        var serverResponse = new Dictionary<string, object>
        {
            ["results"] = new List<object>
                {
                    new Dictionary<string, object> { ["objectId"] = "obj1" },
                    new Dictionary<string, object> { ["objectId"] = "obj2" }
                }
        };
        var tupleResponse = new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, serverResponse);

        mockCommandRunner.Setup(runner => runner.RunCommandAsync(It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tupleResponse);

        // Mock the decoder to return a state for each object.
        mockDecoder.Setup(d => d.Decode(It.IsAny<object>())).Returns<IDictionary<string, object>>(data => new MutableObjectState { ObjectId = data["objectId"].ToString() });

        // Act
        var result = await controller.FindAsync(query, null, CancellationToken.None);

        // Assert
        Assert.AreEqual(2, result.Count());
        Assert.AreEqual("obj1", result.First().ObjectId);
        mockCommandRunner.Verify(r => r.RunCommandAsync(It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()), Times.Once);
        mockDecoder.Verify(d => d.Decode(It.IsAny<object>()), Times.Exactly(2));
    }

    [TestMethod]
    [Description("Tests that FindAsync returns an empty enumerable when the server returns no results.")]
    public async Task FindAsync_WithEmptyResults_ReturnsEmptyEnumerable()
    {
        // Arrange
        var controller = new ParseQueryController(mockCommandRunner.Object, mockDecoder.Object);
        var query = new ParseQuery<ParseObject>(client.Services, "TestClass");
        var serverResponse = new Dictionary<string, object> { ["results"] = new List<object>() };
        var tupleResponse = new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, serverResponse);

        mockCommandRunner.Setup(runner => runner.RunCommandAsync(It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tupleResponse);

        // Act
        var result = await controller.FindAsync(query, null, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count());
    }

    [TestMethod]
    [Description("Tests that CountAsync builds the correct command with limit=0 and count=1.")]
    public async Task CountAsync_BuildsCorrectCommandAndReturnsCount()
    {
        // Arrange
        var controller = new ParseQueryController(mockCommandRunner.Object, mockDecoder.Object);
        var query = new ParseQuery<ParseObject>(client.Services, "TestClass");
        var serverResponse = new Dictionary<string, object> { ["count"] = 150 };
        var tupleResponse = new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, serverResponse);

        ParseCommand capturedCommand = null;
        mockCommandRunner.Setup(runner => runner.RunCommandAsync(It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()))
            .Callback<ParseCommand, IProgress<IDataTransferLevel>, IProgress<IDataTransferLevel>, CancellationToken>((cmd, _, __, ___) => capturedCommand = cmd)
            .ReturnsAsync(tupleResponse);

        // Act
        var result = await controller.CountAsync(query, null, CancellationToken.None);

        // Assert
        Assert.AreEqual(150, result);
        Assert.IsNotNull(capturedCommand);
        StringAssert.Contains(capturedCommand.Path, "limit=0");
        StringAssert.Contains(capturedCommand.Path, "count=1");
    }

    [TestMethod]
    [Description("Tests that FirstAsync builds the correct command with limit=1 and returns one object.")]
    public async Task FirstAsync_BuildsCorrectCommandAndReturnsOneState()
    {
        // Arrange
        var controller = new ParseQueryController(mockCommandRunner.Object, mockDecoder.Object);
        var query = new ParseQuery<ParseObject>(client.Services, "TestClass");
        var serverResponse = new Dictionary<string, object>
        {
            ["results"] = new List<object> { new Dictionary<string, object> { ["objectId"] = "theFirst" } }
        };
        var tupleResponse = new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, serverResponse);

        ParseCommand capturedCommand = null;
        mockCommandRunner.Setup(runner => runner.RunCommandAsync(It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()))
            .Callback<ParseCommand, IProgress<IDataTransferLevel>, IProgress<IDataTransferLevel>, CancellationToken>((cmd, _, __, ___) => capturedCommand = cmd)
            .ReturnsAsync(tupleResponse);

        mockDecoder.Setup(d => d.Decode(It.IsAny<object>())).Returns(new MutableObjectState { ObjectId = "theFirst" });

        // Act
        var result = await controller.FirstAsync(query, null, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("theFirst", result.ObjectId);
        Assert.IsNotNull(capturedCommand);
        StringAssert.Contains(capturedCommand.Path, "limit=1");
        mockDecoder.Verify(d => d.Decode(It.IsAny<object>()), Times.Once);
    }

    [TestMethod]
    [Description("Tests that FindAsync throws an exception for ACL query errors.")]
    public async Task FindAsync_WhenServerReturnsAclError_ThrowsException()
    {
        // Arrange
        var controller = new ParseQueryController(mockCommandRunner.Object, mockDecoder.Object);
        var query = new ParseQuery<ParseObject>(client.Services, "TestClass");
        var serverResponse = new Dictionary<string, object> { ["code"] = 102L, ["error"] = "Cannot query on ACL." };
        var tupleResponse = new Tuple<System.Net.HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.BadRequest, serverResponse);

        mockCommandRunner.Setup(runner => runner.RunCommandAsync(It.IsAny<ParseCommand>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<IProgress<IDataTransferLevel>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tupleResponse);

        // Act & Assert
        var ex = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => controller.FindAsync(query, null, CancellationToken.None));
        StringAssert.Contains(ex.Message, "Cannot query on ACL");
    }

[ParseClassName(nameof(SubClass))]
    class SubClass : ParseObject { }

    [ParseClassName(nameof(UnregisteredSubClass))]
    class UnregisteredSubClass : ParseObject { }
    private ParseClient Client { get; set; }

    [TestInitialize]
    public void SetUp()
    {
         Client.Publicize();
        // Register the valid classes
        Client.RegisterSubclass(typeof(ParseSession));
        Client.RegisterSubclass(typeof(ParseUser));

        mockCommandRunner = new Mock<IParseCommandRunner>();
        mockDecoder = new Mock<IParseDataDecoder>();
        var serviceHub = new MutableServiceHub
        {
            CommandRunner = mockCommandRunner.Object,
            Decoder = mockDecoder.Object,
            // Add other necessary services for ParseQuery constructor if needed
            ClassController = new Platform.Objects.ParseObjectClassController()
        };
        serviceHub.SetDefaults();
        client = new ParseClient(new ServerConnectionData { Test = true }, serviceHub);
    }
    [TestMethod]
    [Description("Tests that CountAsync calls IParseCommandRunner and returns integer")]
    public async Task CountAsync_CallsRunnerAndReturnsCount()// Mock difficulty: 2
    {
        //Arrange
        var mockRunner = new Mock<IParseCommandRunner>();
        var mockDecoder = new Mock<IParseDataDecoder>();
        var mockState = new Mock<IObjectState>();
        var mockUser = new Mock<ParseUser>();
        mockRunner.Setup(c => c.RunCommandAsync(It.IsAny<ParseCommand>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tuple<HttpStatusCode, IDictionary<string, object>>(System.Net.HttpStatusCode.OK, new Dictionary<string, object> { { "count", 10 } }));

        ParseQueryController controller = new(mockRunner.Object, mockDecoder.Object);
        var query = Client.GetQuery("TestClass");
        //Act
        int result = await controller.CountAsync(query, mockUser.Object, CancellationToken.None);

        //Assert
        mockRunner.Verify(runner => runner.RunCommandAsync(It.IsAny<ParseCommand>(), null, null, It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreEqual(10, result);

    }

    [TestMethod]
    [Description("Tests that a LINQ '==' operator is translated to WhereEqualTo.")]
    public void Where_EqualsOperator_TranslatesToWhereEqualTo()
    {
        var query = new ParseQuery<ParseObject>(Client.Services, "TestClass");
        var resultQuery = query.Where(obj => obj.Get<string>("name") == "Zeke");
        Assert.AreEqual("Zeke", resultQuery.GetConstraint("name"));
    }

    [TestMethod]
    [Description("Tests that a LINQ '>' operator is translated to WhereGreaterThan.")]
    public void Where_GreaterThanOperator_TranslatesToWhereGreaterThan()
    {
        var query = new ParseQuery<ParseObject>(Client.Services, "TestClass");
        var resultQuery = query.Where(obj => obj.Get<int>("score") > 1000);
        var constraint = resultQuery.GetConstraint("score") as IDictionary<string, object>;
        Assert.AreEqual(1000, constraint["$gt"]);
    }

    [TestMethod]
    [Description("Tests that string.StartsWith is translated to WhereStartsWith.")]
    public void Where_StringStartsWith_TranslatesToWhereStartsWith()
    {
        var query = new ParseQuery<ParseObject>(Client.Services, "TestClass");
        var resultQuery = query.Where(obj => obj.Get<string>("name").StartsWith("Z"));
        var constraint = resultQuery.GetConstraint("name") as IDictionary<string, object>;
        Assert.IsTrue((constraint["$regex"] as string).StartsWith("^\\QZ"));
    }

    [TestMethod]
    [Description("Tests that a collection.Contains(value) is translated to WhereEqualTo.")]
    public void Where_CollectionContainsValue_TranslatesToWhereEqualTo()
    {
        var query = new ParseQuery<ParseObject>(Client.Services, "TestClass");
        var resultQuery = query.Where(obj => obj.Get<IList<string>>("tags").Contains("awesome"));
        Assert.AreEqual("awesome", resultQuery.GetConstraint("tags"));
    }

    [TestMethod]
    [Description("Tests that a value.In(collection) is translated to WhereContainedIn.")]
    public void Where_ValueInCollection_TranslatesToWhereContainedIn()
    {
        var names = new[] { "Zeke", "Midas" };
        var query = new ParseQuery<ParseObject>(Client.Services, "TestClass");
        var resultQuery = query.Where(obj => names.Contains(obj.Get<string>("name")));
        var constraint = resultQuery.GetConstraint("name") as IDictionary<string, object>;
        Assert.IsNotNull(constraint["$in"]);
        Assert.AreEqual(2, (constraint["$in"] as IEnumerable<object>).Count());
    }

    [TestMethod]
    [Description("Tests that OrderBy with a property is translated correctly.")]
    public void OrderBy_Property_TranslatesToOrderBy()
    {
        var query = new ParseQuery<ParseObject>(Client.Services, "TestClass");
        var resultQuery = query.OrderBy(obj => obj.CreatedAt);
        var parameters = resultQuery.BuildParameters();
        Assert.AreEqual("createdAt", parameters["order"]);
    }

    [TestMethod]
    [Description("Tests that a complex LINQ expression with '&&' is translated correctly.")]
    public void Where_AndAlsoOperator_TranslatesToMultipleConstraints()
    {
        var query = new ParseQuery<ParseObject>(Client.Services, "Player");
        var resultQuery = query.Where(p => p.Get<int>("score") > 100 && p.Get<bool>("active") == true);
        var scoreConstraint = resultQuery.GetConstraint("score") as IDictionary<string, object>;
        var activeConstraint = resultQuery.GetConstraint("active");

        Assert.AreEqual(100, scoreConstraint["$gt"]);
        Assert.AreEqual(true, activeConstraint);
    }

    [TestMethod]
    [Description("Tests that a LINQ expression with '||' is translated to an $or query.")]
    public void Where_OrElseOperator_TranslatesToOrQuery()
    {
        // Arrange
        var query = new ParseQuery<ParseObject>(Client.Services, "Player");

        // Act
        var resultQuery = query.Where(p => p.Get<int>("wins") > 100 || p.Get<int>("losses") == 0);
        var parameters = resultQuery.BuildParameters();
        var where = parameters["where"] as IDictionary<string, object>;
        var orClauses = where["$or"] as IList<object>;

        // Assert
        Assert.IsNotNull(orClauses);
        Assert.AreEqual(2, orClauses.Count);

        var winsClause = orClauses[0] as IDictionary<string, object>;
        var lossesClause = orClauses[1] as IDictionary<string, object>;

        var winsGt = winsClause["wins"] as IDictionary<string, object>;
        Assert.AreEqual(100, winsGt["$gt"]);
        Assert.AreEqual(0, lossesClause["losses"]);
    }
}
