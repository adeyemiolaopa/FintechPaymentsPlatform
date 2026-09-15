using Microsoft.EntityFrameworkCore;
using Payments.Service.Template.Application.Examples;
using Payments.Service.Template.Domain.Examples;

namespace Payments.Service.Template.Infrastructure.Persistence;

public sealed class ExampleRepository : IExampleRepository
{
    private readonly TemplateDbContext _dbContext;

    public ExampleRepository(TemplateDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Example example, CancellationToken cancellationToken = default)
    {
        await _dbContext.Examples.AddAsync(example, cancellationToken).ConfigureAwait(false);
    }

    public Task<Example?> GetByIdAsync(ExampleId id, CancellationToken cancellationToken = default)
    {
        return _dbContext.Examples.SingleOrDefaultAsync(example => example.Id == id, cancellationToken);
    }

    public Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return _dbContext.Examples.AnyAsync(example => example.Email == email, cancellationToken);
    }
}
