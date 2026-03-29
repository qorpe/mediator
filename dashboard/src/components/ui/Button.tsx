import { cn } from "@/lib/utils";
import type { ButtonHTMLAttributes } from "react";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: "primary" | "secondary" | "ghost";
  size?: "sm" | "md" | "lg";
}

export function Button({
  className,
  variant = "primary",
  size = "md",
  children,
  ...props
}: ButtonProps) {
  return (
    <button
      className={cn(
        "inline-flex items-center justify-center gap-2 rounded-xl font-medium transition-all duration-200 cursor-pointer whitespace-nowrap",
        variant === "primary" &&
          "bg-brand-600 text-white hover:bg-brand-700 shadow-md shadow-brand-600/25 hover:shadow-lg hover:shadow-brand-600/30",
        variant === "secondary" &&
          "bg-surface-alt text-text-primary border border-border hover:bg-surface-hover hover:border-brand-200 dark:hover:border-brand-800",
        variant === "ghost" &&
          "text-text-secondary hover:text-text-primary hover:bg-surface-hover",
        size === "sm" && "px-3 py-1.5 text-sm",
        size === "md" && "px-5 py-2.5 text-sm",
        size === "lg" && "px-7 py-3.5 text-base",
        className,
      )}
      {...props}
    >
      {children}
    </button>
  );
}
