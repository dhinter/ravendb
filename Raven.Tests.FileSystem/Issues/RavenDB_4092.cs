﻿// -----------------------------------------------------------------------
//  <copyright file="RavenDB_4092.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Raven.Abstractions.Data;
using Raven.Abstractions.Database.Smuggler;
using Raven.Abstractions.Database.Smuggler.FileSystem;
using Raven.Abstractions.FileSystem;
using Raven.Database.FileSystem.Smuggler;
using Raven.Database.FileSystem.Smuggler.Embedded;
using Raven.Smuggler.FileSystem;
using Raven.Smuggler.FileSystem.Streams;
using Raven.Tests.Helpers;

using Xunit;
using Xunit.Extensions;

namespace Raven.Tests.FileSystem.Issues
{
    public class RavenDB_4092 : RavenFilesTestBase
    {
        [Fact]
        public async Task stream_cannot_contains_neither_files_scheduled_for_deletion_nor_tombstones()
        {
            using (var store = this.NewStore())
            {
                Etag fromEtag = Etag.Empty;

                using (var session = store.OpenAsyncSession())
                {
                    for (int i = 0; i < 10; i++)
                        session.RegisterUpload(i + ".file", CreateUniformFileStream(10));

                    await session.SaveChangesAsync();
                }

                using (var session = store.OpenAsyncSession())
                {
                    session.RegisterFileDeletion("3.file");

                    await session.SaveChangesAsync();
                }

                int count = 0;
                using (var session = store.OpenAsyncSession())
                {
                    using (var reader = await session.Commands.StreamFileHeadersAsync(fromEtag: fromEtag, pageSize: 100))
                    {
                        while (await reader.MoveNextAsync())
                        {
                            count++;
                            Assert.IsType<FileHeader>(reader.Current);
                        }
                    }
                }
                Assert.Equal(9, count);
            }
        }

        [Fact]
        public async Task starts_with_results_cannot_contains_neither_files_scheduled_for_deletion_nor_tombstones()
        {
            using (var store = this.NewStore())
            {
                using (var session = store.OpenAsyncSession())
                {
                    for (int i = 0; i < 10; i++)
                        session.RegisterUpload("prefix-" + i + ".file", CreateUniformFileStream(10));

                    await session.SaveChangesAsync();
                }

                using (var session = store.OpenAsyncSession())
                {
                    session.RegisterFileDeletion("prefix-3.file");

                    await session.SaveChangesAsync();
                }

                Assert.Equal(9, (await store.AsyncFilesCommands.StartsWithAsync("prefix", null, 0, 100)).Length);
            }
        }

        [Theory]
        [PropertyData("Storages")]
        public async Task export_result_cannot_contain_neither_files_scheduled_for_deletion_nor_tombstones(string storage)
        {
            var exportStream = new MemoryStream();

            using (var store = this.NewStore(requestedStorage: storage))
            {
                using (var session = store.OpenAsyncSession())
                {
                    for (int i = 0; i < 10; i++)
                        session.RegisterUpload("prefix-" + i + ".file", CreateUniformFileStream(10));

                    await session.SaveChangesAsync();
                }

                using (var session = store.OpenAsyncSession())
                {
                    session.RegisterFileDeletion("prefix-3.file");

                    await session.SaveChangesAsync();
                }

                var fs = GetFileSystem();

                var exporter = new FileSystemSmuggler(new FileSystemSmugglerOptions());
                await exporter
                    .ExecuteAsync(new EmbeddedSmugglingSource(fs), new StreamSmugglingDestination(exportStream, leaveOpen: true))
                    .ConfigureAwait(false);

                using (var import = NewStore(1))
                {
                    exportStream.Position = 0;

                    var importedFs = GetFileSystem(1);
                    var importer = new FileSystemSmuggler(new FileSystemSmugglerOptions());
                    await importer
                        .ExecuteAsync(new StreamSmugglingSource(exportStream), new EmbeddedSmugglingDestination(importedFs))
                        .ConfigureAwait(false);

                    importedFs.Storage.Batch(accessor =>
                    {
                        Assert.Equal(9, accessor.GetFilesAfter(Etag.Empty, 100).Count());
                    });
                }
            }
        }
    }
}