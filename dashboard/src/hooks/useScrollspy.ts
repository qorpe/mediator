import { useEffect, useState } from "react";

export function useScrollspy(ids: string[], offset = 100) {
  const [activeId, setActiveId] = useState<string>("");

  useEffect(() => {
    const handler = () => {
      const scrollY = window.scrollY + offset;
      let current = "";

      for (const id of ids) {
        const el = document.getElementById(id);
        if (el && el.offsetTop <= scrollY) {
          current = id;
        }
      }

      setActiveId(current);
    };

    handler();
    window.addEventListener("scroll", handler, { passive: true });
    return () => window.removeEventListener("scroll", handler);
  }, [ids, offset]);

  return activeId;
}
