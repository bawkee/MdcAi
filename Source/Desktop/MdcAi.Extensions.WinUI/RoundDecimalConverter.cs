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

using Microsoft.UI.Xaml.Data;
using SalaTools.Core;

public class RoundDecimalConverter : IValueConverter
{
    public int Decimals { get; set; } = 2;

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value.ChangeType<decimal>().Round(Decimals).ChangeType(targetType);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value.ChangeType(targetType);
}