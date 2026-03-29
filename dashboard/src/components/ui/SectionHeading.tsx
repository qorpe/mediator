import { cn } from "@/lib/utils";

interface SectionHeadingProps {
  title: string;
  subtitle?: string;
  className?: string;
  align?: "left" | "center";
}

export function SectionHeading({
  title,
  subtitle,
  className,
  align = "center",
}: SectionHeadingProps) {
  return (
    <div
      className={cn(
        "max-w-3xl mb-16",
        align === "center" && "mx-auto text-center",
        className,
      )}
    >
      <h2 className="text-3xl sm:text-4xl font-bold tracking-tight text-text-primary mb-4">
        {title}
      </h2>
      {subtitle && (
        <p className="text-lg text-text-secondary leading-relaxed">
          {subtitle}
        </p>
      )}
    </div>
  );
}
