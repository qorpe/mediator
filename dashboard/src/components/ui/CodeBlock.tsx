import { useState, useCallback } from "react";
import { Copy, Check } from "lucide-react";
import { cn } from "@/lib/utils";
import { useTranslation } from "react-i18next";

interface CodeBlockProps {
  code: string;
  language?: string;
  className?: string;
  showCopy?: boolean;
}

export function CodeBlock({
  code,
  language = "csharp",
  className,
  showCopy = true,
}: CodeBlockProps) {
  const [copied, setCopied] = useState(false);
  const { t } = useTranslation();

  const handleCopy = useCallback(() => {
    navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }, [code]);

  return (
    <div
      className={cn(
        "relative group rounded-xl overflow-hidden border border-border",
        className,
      )}
    >
      {language && (
        <div className="flex items-center justify-between px-4 py-2 bg-surface-hover border-b border-border">
          <span className="text-xs text-text-tertiary font-mono">
            {language}
          </span>
          {showCopy && (
            <button
              onClick={handleCopy}
              className="flex items-center gap-1.5 text-xs text-text-tertiary hover:text-text-primary transition-colors cursor-pointer"
              aria-label="Copy code"
            >
              {copied ? (
                <>
                  <Check className="w-3.5 h-3.5" />
                  <span>{t("quickStart.copied")}</span>
                </>
              ) : (
                <Copy className="w-3.5 h-3.5" />
              )}
            </button>
          )}
        </div>
      )}
      <pre className="p-4 overflow-x-auto bg-[#0d1117] dark:bg-[#0a0e17] text-sm leading-relaxed">
        <code className="text-slate-300 font-mono">{code}</code>
      </pre>
    </div>
  );
}
