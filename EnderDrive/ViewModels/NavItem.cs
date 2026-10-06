using System;
using System.Collections.Generic;
using System.Text;

namespace EnderDrive.ViewModels;

public record NavItem(string Icon, string Label, ViewModelBase Page);