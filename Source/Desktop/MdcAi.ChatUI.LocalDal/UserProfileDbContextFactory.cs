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

namespace MdcAi.ChatUI.LocalDal;

using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Lets `dotnet ef` create a context for migrations without bootstrapping the WinUI shell.
/// A throw-away in-memory connection is enough: migrations operate on the model snapshot and
/// `database update` gets its own connection string from the caller.
/// </summary>
public class UserProfileDbContextFactory : IDesignTimeDbContextFactory<UserProfileDbContext>
{
    public UserProfileDbContext CreateDbContext(string[] args) => new("design-time.db");
}