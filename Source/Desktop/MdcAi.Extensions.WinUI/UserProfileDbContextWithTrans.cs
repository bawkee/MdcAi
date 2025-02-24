#region Copyright Notice
// Copyright (c) 2023 Bojan Sala
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//      http: www.apache.org/licenses/LICENSE-2.0
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
#endregion

namespace MdcAi.Extensions.WinUI;
using ChatUI.LocalDal;
using Microsoft.EntityFrameworkCore.Storage;
using System.Reactive.Disposables;

public class UserProfileDbContextWithTrans : IDisposable
{
    private readonly CompositeDisposable _cd;
    public IDbContextTransaction Trans { get; }
    public UserProfileDbContext Ctx { get; }

    public UserProfileDbContextWithTrans(UserProfileDbContext ctx)
    {
        Ctx = ctx;
        Trans = Ctx.Database.BeginTransaction();
        _cd = new(Trans, Ctx);
    }

    public void Dispose() => _cd.Dispose();
}