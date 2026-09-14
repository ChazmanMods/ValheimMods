using System;

namespace RunicDisplayStands
{
    // A synchronous local transaction. The persistent commit must be all-or-rollback;
    // no RPC, world drops, equipment effects, or asynchronous work belong in it.
    internal static class TransferBoundary
    {
        internal static bool Complete(bool actionSucceeded, Func<bool> validate,
            Func<bool> commit, Action rollback)
        {
            bool committed = false;
            try
            {
                if (actionSucceeded && validate()) committed = commit();
                return committed;
            }
            finally
            {
                if (!committed) rollback();
            }
        }
    }
}
