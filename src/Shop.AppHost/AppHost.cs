var builder = DistributedApplication.CreateBuilder(args);

var antipatterns = builder.AddProject<Projects.Shop_AntiPatterns>("antipatterns")
    .WithHttpHealthCheck("/health");

var bestpractice = builder.AddProject<Projects.Shop_BestPractice>("bestpractice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.Shop_Web>("web")
    .WithReference(antipatterns)
    .WithReference(bestpractice)
    .WaitFor(antipatterns)
    .WaitFor(bestpractice)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
