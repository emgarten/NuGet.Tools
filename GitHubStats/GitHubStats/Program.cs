using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitHubStats
{
    class Program
    {
        static DateTimeOffset _start = DateTimeOffset.Parse("2016");

        static void Main(string[] args)
        {
            Run().Wait();
        }

        static async Task Run()
        {
            HashSet<int> uniqueComments = new HashSet<int>();
            int total = 0;

            try
            {
                var github = new GitHubClient(new ProductHeaderValue("EmgartenGithubViewer"));
                //github.Credentials = new Credentials();

                var issueRequest = new RepositoryIssueRequest();
                issueRequest.State = ItemStateFilter.All;
                var issues = await github.Issue.GetAllForRepository("NuGet", "NuGet.Client", issueRequest);
                IssueCommentRequest request = new IssueCommentRequest();
                request.Since = _start;
                ApiOptions options = new ApiOptions();

                foreach (var issue in issues)
                {
                    if (!StringComparer.OrdinalIgnoreCase.Equals("emgarten", issue.User.Login))
                    {
                        var comments = await github.Issue.Comment.GetAllForIssue("NuGet", "NuGet.Client", issue.Number);

                        foreach (var comment in comments)
                        {
                            if (comment.UpdatedAt.HasValue && comment.UpdatedAt.Value >= _start)
                            {
                                if (StringComparer.OrdinalIgnoreCase.Equals("emgarten", comment.User.Login))
                                {
                                    total++;
                                    uniqueComments.Add(issue.Number);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Debug here.
                throw ex;
            }
        }
    }
}
