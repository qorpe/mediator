import { Header } from "@/components/layout/Header";
import { Footer } from "@/components/layout/Footer";
import { HeroSection } from "@/components/sections/HeroSection";
import { FeaturesSection } from "@/components/sections/FeaturesSection";
import { BenchmarksSection } from "@/components/sections/BenchmarksSection";
import { PipelineSection } from "@/components/sections/PipelineSection";
import { ComparisonSection } from "@/components/sections/ComparisonSection";
import { QuickStartSection } from "@/components/sections/QuickStartSection";
import { useTheme } from "@/hooks/useTheme";
import { useDirection } from "@/hooks/useDirection";

export default function App() {
  const { theme, toggleTheme } = useTheme();
  useDirection();

  return (
    <div className="min-h-screen bg-surface text-text-primary overflow-x-hidden">
      <Header theme={theme} onToggleTheme={toggleTheme} />
      <main>
        <HeroSection />
        <FeaturesSection />
        <BenchmarksSection />
        <PipelineSection />
        <ComparisonSection />
        <QuickStartSection />
      </main>
      <Footer />
    </div>
  );
}
