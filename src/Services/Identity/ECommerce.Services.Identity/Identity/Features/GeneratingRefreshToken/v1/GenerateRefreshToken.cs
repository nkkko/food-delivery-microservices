using Ardalis.GuardClauses;
using BuildingBlocks.Abstractions.CQRS.Commands;
using BuildingBlocks.Core.Utils;
using ECommerce.Services.Identity.Identity.Dtos;
using ECommerce.Services.Identity.Identity.Dtos.v1;
using ECommerce.Services.Identity.Identity.Features.RefreshingToken;
using ECommerce.Services.Identity.Identity.Features.RefreshingToken.v1;
using ECommerce.Services.Identity.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Services.Identity.Identity.Features.GeneratingRefreshToken.v1;

public record GenerateRefreshToken(Guid UserId, string? Token = null) : ICommand<GenerateRefreshTokenResponse>;

public class GenerateRefreshTokenHandler : ICommandHandler<GenerateRefreshToken, GenerateRefreshTokenResponse>
{
    private readonly IdentityContext _context;

    public GenerateRefreshTokenHandler(IdentityContext context)
    {
        _context = context;
    }

    public async Task<GenerateRefreshTokenResponse> Handle(
        GenerateRefreshToken request,
        CancellationToken cancellationToken)
    {
        Guard.Against.Null(request, nameof(GenerateRefreshToken));

        var refreshToken = await _context.Set<Shared.Models.RefreshToken>()
            .FirstOrDefaultAsync(
                rt => rt.UserId == request.UserId && rt.Token == request.Token,
                cancellationToken);

        if (refreshToken == null)
        {
            var token = Shared.Models.RefreshToken.GetRefreshToken();

            refreshToken = new Shared.Models.RefreshToken
            {
                UserId = request.UserId,
                Token = token,
                CreatedAt = DateTime.Now,
                ExpiredAt = DateTime.Now.AddDays(1),
                CreatedByIp = IpUtilities.GetIpAddress()
            };

            await _context.Set<Shared.Models.RefreshToken>().AddAsync(refreshToken, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (!refreshToken.IsRefreshTokenValid())
                throw new InvalidRefreshTokenException(refreshToken);

            var token = Shared.Models.RefreshToken.GetRefreshToken();

            refreshToken.Token = token;
            refreshToken.ExpiredAt = DateTime.Now;
            refreshToken.CreatedAt = DateTime.Now.AddDays(10);
            refreshToken.CreatedByIp = IpUtilities.GetIpAddress();

            _context.Set<Shared.Models.RefreshToken>().Update(refreshToken);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // remove old refresh tokens from user
        // we could also maintain them on the database with changing their revoke date
        await RemoveOldRefreshTokens(request.UserId);

        return new GenerateRefreshTokenResponse(new RefreshTokenDto
        {
            Token = refreshToken.Token,
            CreatedAt = refreshToken.CreatedAt,
            ExpireAt = refreshToken.ExpiredAt,
            UserId = refreshToken.UserId,
            CreatedByIp = refreshToken.CreatedByIp,
            IsActive = refreshToken.IsActive,
            IsExpired = refreshToken.IsExpired,
            IsRevoked = refreshToken.IsRevoked,
            RevokedAt = refreshToken.RevokedAt
        });
    }


    private Task RemoveOldRefreshTokens(Guid userId, long? ttlRefreshToken = null)
    {
        var refreshTokens = _context.Set<global::ECommerce.Services.Identity.Shared.Models.RefreshToken>()
            .Where(rt => rt.UserId == userId);

        refreshTokens.ToList().RemoveAll(x => !x.IsRefreshTokenValid(ttlRefreshToken));

        return _context.SaveChangesAsync();
    }
}
