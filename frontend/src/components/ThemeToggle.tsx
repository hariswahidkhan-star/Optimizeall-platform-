import { Laptop, Moon, Sun } from 'lucide-react';
import { useTheme, type ThemePreference } from '@/lib/theme/themeContext';
import { DropdownMenu } from './ui/DropdownMenu';
import { IconButton } from './ui/IconButton';

const OPTIONS: { value: ThemePreference; label: string; icon: typeof Sun }[] = [
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
  { value: 'system', label: 'Match system', icon: Laptop },
];

/** Theme picker: light, dark or follow the operating system (persisted per browser). */
export function ThemeToggle() {
  const { preference, resolved, setPreference } = useTheme();
  return (
    <DropdownMenu
      label="Theme"
      trigger={
        <IconButton
          label={`Theme: ${OPTIONS.find((o) => o.value === preference)?.label ?? 'Match system'}`}
          icon={resolved === 'dark' ? <Moon /> : <Sun />}
        />
      }
      items={OPTIONS.map((option) => {
        const Icon = option.icon;
        return {
          id: option.value,
          label: option.label,
          icon: <Icon />,
          current: preference === option.value,
          onSelect: () => setPreference(option.value),
        };
      })}
    />
  );
}
