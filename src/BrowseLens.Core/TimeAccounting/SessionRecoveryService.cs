using System;
using System.Threading.Tasks;
using BrowseLens.Core.Repositories;

namespace BrowseLens.Core.TimeAccounting
{
    public class SessionRecoveryService
    {
        private readonly IBrowsingSessionRepository _sessionRepository;

        public SessionRecoveryService(IBrowsingSessionRepository sessionRepository)
        {
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        }

        /// <summary>
        /// Scans for and cleanly closes any sessions left open from an ungraceful shutdown or crash.
        /// Ensures unobserved application downtime is never counted as active foreground browsing.
        /// </summary>
        public async Task<long> RecoverOrphanedSessionsAsync(DateTime startupTimeUtc)
        {
            // Close all open sessions left dangling before this startup
            long closedCount = await _sessionRepository.CloseOrphanedSessionsAsync(
                startupTimeUtc,
                "AppCrashRecovery");

            return closedCount;
        }
    }
}

