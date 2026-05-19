import { type ReactElement, useCallback } from 'react'
import { useDropzone } from 'react-dropzone'

export type UploadState =
  | { status: 'idle' }
  | { status: 'uploading'; progress: number }
  | { status: 'done'; blobName: string; fileName: string }
  | { status: 'error'; message: string }

interface FileDropzoneProps {
  uploadState: UploadState
  onFileDrop: (file: File) => void
}

export default function FileDropzone({ uploadState, onFileDrop }: FileDropzoneProps): ReactElement {
  const onDrop = useCallback(
    (accepted: File[]) => {
      if (accepted[0]) onFileDrop(accepted[0])
    },
    [onFileDrop],
  )

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    accept: { 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': ['.xlsx'] },
    maxFiles: 1,
    maxSize: 10 * 1024 * 1024,
    disabled: uploadState.status === 'uploading' || uploadState.status === 'done',
  })

  const baseClass = 'rounded-[9px] border-2 border-dashed p-8 text-center transition-all'

  const stateClass = (() => {
    if (uploadState.status === 'done')      return 'border-[#12E58D] bg-[#12E58D]/5 cursor-default'
    if (uploadState.status === 'error')     return 'border-red-400 bg-red-50 cursor-pointer'
    if (uploadState.status === 'uploading') return 'border-brand-border bg-brand-surface cursor-default'
    if (isDragActive)                        return 'border-[#0072ff] bg-[#0072ff]/5 cursor-copy'
    return 'border-brand-border hover:border-[#0072ff]/50 hover:bg-[#0072ff]/[0.02] cursor-pointer'
  })()

  return (
    <div {...getRootProps()} className={`${baseClass} ${stateClass}`}>
      <input {...getInputProps()} />

      {uploadState.status === 'idle' && (
        <div className="flex flex-col items-center gap-2">
          <div className="w-10 h-10 rounded-full bg-brand-surface border border-brand-border flex items-center justify-center mb-1">
            <svg className={`w-5 h-5 ${isDragActive ? 'text-[#0072ff]' : 'text-brand-muted'} transition-colors`}
              fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
                d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
            </svg>
          </div>
          <p className="text-sm font-semibold text-[#222222]">
            {isDragActive ? 'Drop your Excel file here' : 'Drag & drop an Excel file, or click to browse'}
          </p>
          <p className="text-xs text-brand-muted">.xlsx only · max 10 MB</p>
        </div>
      )}

      {uploadState.status === 'uploading' && (
        <div className="flex flex-col items-center gap-2">
          <svg className="w-6 h-6 animate-spin text-[#0072ff]" fill="none" viewBox="0 0 24 24">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
          </svg>
          <p className="text-sm text-[#0072ff] font-semibold">
            Uploading{uploadState.progress > 0 ? ` ${uploadState.progress}%` : '…'}
          </p>
        </div>
      )}

      {uploadState.status === 'done' && (
        <div className="flex flex-col items-center gap-2">
          <div className="w-10 h-10 rounded-full bg-[#12E58D]/15 flex items-center justify-center mb-1">
            <svg className="w-5 h-5 text-[#0a7a4c]" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
            </svg>
          </div>
          <p className="text-sm font-semibold text-[#0a7a4c]">{uploadState.fileName}</p>
          <p className="text-xs text-brand-muted">File uploaded successfully — drop a new file to replace.</p>
        </div>
      )}

      {uploadState.status === 'error' && (
        <div className="flex flex-col items-center gap-1.5">
          <div className="w-10 h-10 rounded-full bg-red-50 flex items-center justify-center mb-1">
            <svg className="w-5 h-5 text-red-500" fill="none" stroke="currentColor" viewBox="0 0 24 24" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </div>
          <p className="text-sm font-semibold text-red-700">Upload failed</p>
          <p className="text-xs text-red-500">{uploadState.message}</p>
          <p className="mt-1 text-xs text-brand-muted">Click or drop to try again.</p>
        </div>
      )}
    </div>
  )
}
