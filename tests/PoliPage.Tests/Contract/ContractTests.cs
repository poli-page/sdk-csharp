using Xunit;
using Xunit.Abstractions;

namespace PoliPage.Tests.Contract;

// Conformance with the Poli Page API contract (https://github.com/poli-page/api-contract).
//
// CI copies the pinned contract.json with the poli-page/api-contract action and sets
// POLI_CONTRACT to its path. Every `kind: "sdk"` case runs against a local HTTP server
// (MockServer) that answers the case's responses in order and records each request; the
// test then compares the recorded requests, the OnRequest / OnRetry events and the
// normalized result with the case's `expect` (ContractCaseRunner). Each case runs twice:
// with the HttpClients the SDK creates, and with one injected HttpClient.
//
// tests/contract-skip.json maps the case ids allowed to fail to the reason. A listed case
// that passes, or a listed id the contract does not have, fails the run.
//
// Locally: `POLI_CONTRACT=/path/to/contract.json dotnet test`. Without it the suite is
// skipped, except when CI is set, where a missing contract is an error.

/// <summary>Skips the contract suite locally when no contract is given.</summary>
public sealed class ContractFactAttribute : FactAttribute
{
    public ContractFactAttribute()
    {
        if (ContractFile.LocalSkipReason is { } reason)
            Skip = reason;
    }
}

/// <summary>Skips the contract suite locally when no contract is given.</summary>
public sealed class ContractTheoryAttribute : TheoryAttribute
{
    public ContractTheoryAttribute()
    {
        if (ContractFile.LocalSkipReason is { } reason)
            Skip = reason;
    }
}

public sealed class ContractTests(ITestOutputHelper output)
{
    /// <summary>The row of a contract that cannot be run, so the failure shows.</summary>
    private const string Unavailable = "(contract unavailable)";

    /// <summary>One row per case id this SDK runs, in contract order.</summary>
    public static TheoryData<string> Cases
    {
        get
        {
            var contract = ContractFile.Current;
            var data = new TheoryData<string>();
            if (contract.UnavailableReason is not null || contract.RunnableIds.Count == 0)
            {
                data.Add(Unavailable);
                return data;
            }

            foreach (var id in contract.RunnableIds)
                data.Add(id);
            return data;
        }
    }

    [ContractFact]
    public void Contract_and_skip_list_are_valid()
    {
        var problems = ContractFile.Current.Problems;
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [ContractTheory]
    [MemberData(nameof(Cases))]
    public Task Case(string id) => RunAsync(id, ClientTransport.SdkOwned);

    [ContractTheory]
    [MemberData(nameof(Cases))]
    public Task Case_through_injected_HttpClient(string id) => RunAsync(id, ClientTransport.Injected);

    private async Task RunAsync(string id, ClientTransport transport)
    {
        var contract = ContractFile.Current;
        if (contract.UnavailableReason is { } reason)
            Assert.Fail(reason);
        Assert.True(contract.SdkCases.TryGetValue(id, out var testCase), $"the contract has no case {id}");

        var problems = await ContractCaseRunner.RunAsync(testCase, transport);
        var title = testCase["title"]?.GetValue<string>() ?? id;
        var listed = contract.SkipList.TryGetValue(id, out var skipReason);
        if (!listed)
        {
            Assert.True(problems.Count == 0, $"{title}\n{string.Join("\n", problems)}");
            return;
        }

        Assert.True(problems.Count > 0, $"{id} passes now: remove it from {ContractFile.SkipFileName}");
        output.WriteLine($"expected to fail ({skipReason}):\n{string.Join("\n", problems)}");
    }
}
