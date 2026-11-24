export function SkeletonLoader() {
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4 xl:grid-cols-8">
        {Array.from({ length: 16 }, (_, index) => (
          <div key={index} className="app-surface h-24 animate-pulse rounded-xl border app-border opacity-70" />
        ))}
      </div>
      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        {[0, 1, 2, 3].map((index) => (
          <div key={index} className="app-surface h-[340px] animate-pulse rounded-xl border app-border opacity-70" />
        ))}
      </div>
      <div className="app-surface h-64 animate-pulse rounded-xl border app-border opacity-70" />
    </div>
  )
}
