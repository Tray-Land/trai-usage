using System.Text;
using TraiUsage.Usage;

namespace TraiUsage.Tests;

[TestClass]
public sealed class CopilotCredentialsTests
{
    [TestMethod]
    public void ReadsTheCliLoginFromItsConfigWithComments()
    {
        const string json = """
            // User settings belong in settings.json.
            // This file is managed automatically.
            {
              "lastLoggedInUser": { "host": "https://github.com", "login": "octocat" },
              "loggedInUsers": [ { "host": "https://github.com", "login": "octocat" } ]
            }
            """;

        (string Host, string Login)? login = CopilotCredentials.ParseCliLogin(json);

        Assert.AreEqual(("https://github.com", "octocat"), login);
        Assert.AreEqual("copilot-cli/https://github.com:octocat", CopilotCredentials.CliCredentialTarget(login!.Value.Host, login.Value.Login));
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("""{ "lastLoggedInUser": { "host": "https://ghe.example.com", "login": "octocat" } }""")]
    [DataRow("""{ "lastLoggedInUser": { "host": "https://github.com" } }""")]
    public void NoLoginForAConfigWithoutAGithubAccount(string json) =>
        Assert.IsNull(CopilotCredentials.ParseCliLogin(json));

    [TestMethod]
    public void ReadsTheEditorTokenForGithubDotCom()
    {
        const string json = """
            {
              "ghe.example.com:abc": { "user": "x", "oauth_token": "ghu_enterprise" },
              "github.com:Iv1.b507a08c87ecfe98": { "user": "octocat", "oauth_token": "gho_editor", "githubAppId": "Iv1.b507a08c87ecfe98" }
            }
            """;

        Assert.AreEqual("gho_editor", CopilotCredentials.ParseEditorToken(json));
    }

    [TestMethod]
    public void NoEditorTokenWhenThereIsNone()
    {
        Assert.IsNull(CopilotCredentials.ParseEditorToken("{}"));
        Assert.Throws<FormatException>(() => CopilotCredentials.ParseEditorToken("{ broken"));
    }

    [TestMethod]
    public void DecodesUtf8AndUtf16Secrets()
    {
        Assert.AreEqual("gho_token", CopilotCredentials.DecodeSecret(Encoding.UTF8.GetBytes("gho_token")));
        Assert.AreEqual("gho_token", CopilotCredentials.DecodeSecret(Encoding.Unicode.GetBytes("gho_token")));
        Assert.IsNull(CopilotCredentials.DecodeSecret([]));
    }

    [TestMethod]
    public void TheCliConfigFollowsCopilotHome()
    {
        Assert.AreEqual(Path.Combine(@"D:\copilot", "config.json"), CopilotCredentials.CliConfigPath(_ => @"D:\copilot"));
    }
}
