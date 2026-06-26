declare module 'pdfmake-rtl/build/pdfmake' {
  export * from 'pdfmake/build/pdfmake';
  export as namespace pdfMake;
}
declare module 'pdfmake-rtl/build/vfs_fonts' {
  const vfs: Record<string, string>;
  export default vfs;
}
