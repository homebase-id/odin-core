﻿using System;
using Microsoft.Data.Sqlite;
using Odin.Core.Storage.SQLite;

namespace Odin.Core.Storage.SQLite.AttestationDatabase
{
    public class TableAttestationRequest : TableAttestationRequestCRUD
    {
        public TableAttestationRequest(AttestationDatabase db, CacheHelper cache) : base(db, cache)
        {
        }

        ~TableAttestationRequest()
        {
        }

        public override void Dispose()
        {
            base.Dispose();
        }
    }
}