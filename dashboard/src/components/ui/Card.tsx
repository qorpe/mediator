import { cn } from "@/lib/utils";
import type { HTMLAttributes } from "react";

interface CardProps extends HTMLAttributes<HTMLDivElement> {
  variant?: "default" | "glass" | "gradient";
  hoverable?: boolean;
}

export function Card({
  className,
  variant = "default",
  hoverable = false,
  children,
  ...props
}: CardProps) {
  return (
    <div
      className={cn(
        "rounded-2xl border border-border p-6 transition-all duration-300",
        variant === "default" &&
          "bg-surface-alt dark:bg-surface-alt",
        variant === "glass" &&
          "bg-white/60 dark:bg-white/5 backdrop-blur-xl border-white/20 dark:border-white/10",
        variant === "gradient" &&
          "gradient-border bg-surface-alt dark:bg-surface-alt",
        hoverable &&
          "hover:shadow-lg hover:shadow-brand-500/5 hover:-translate-y-1 hover:border-brand-200 dark:hover:border-brand-800",
        className,
      )}
      {...props}
    >
      {children}
    </div>
  );
}
