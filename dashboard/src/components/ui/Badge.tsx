import { cn } from "@/lib/utils";
import type { HTMLAttributes } from "react";

interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  variant?: "default" | "success" | "brand";
}

export function Badge({
  className,
  variant = "default",
  children,
  ...props
}: BadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium",
        variant === "default" &&
          "bg-surface-hover text-text-secondary border border-border",
        variant === "success" &&
          "bg-success/10 text-success border border-success/20",
        variant === "brand" &&
          "bg-brand-500/10 text-brand-600 dark:text-brand-400 border border-brand-500/20",
        className,
      )}
      {...props}
    >
      {children}
    </span>
  );
}
