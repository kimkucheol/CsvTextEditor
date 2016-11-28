﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CsvTextEditorService.cs" company="WildGums">
//   Copyright (c) 2008 - 2016 WildGums. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------


namespace Orc.CsvTextEditor.Services
{
    using System;
    using System.Windows;
    using Catel;
    using Catel.IoC;
    using Catel.MVVM;
    using ICSharpCode.AvalonEdit;
    using ICSharpCode.AvalonEdit.Document;
    using Transformers;

    internal class CsvTextEditorService : ICsvTextEditorService
    {
        #region Fields
        private readonly ICommandManager _commandManager;

        private readonly TabSpaceElementGenerator _elementGenerator;
        private readonly HighlightAllOccurencesOfSelectedWordTransformer _highlightAllOccurencesOfSelectedWordTransformer;
        private readonly TextEditor _textEditor;

        private bool _isInCustomUpdate = false;
        private bool _isInRedoUndo = false;

        private int _previousCaretColumn;
        private int _previousCaretLine;
        #endregion

        #region Constructors
        public CsvTextEditorService(TextEditor textEditor, ICommandManager commandManager)
        {
            Argument.IsNotNull(() => textEditor);
            Argument.IsNotNull(() => commandManager);

            _textEditor = textEditor;
            _commandManager = commandManager;

            // Need to make these options accessible to the user in the settings window
            _textEditor.ShowLineNumbers = true;
            _textEditor.Options.HighlightCurrentLine = true;
            _textEditor.Options.ShowEndOfLine = true;
            _textEditor.Options.ShowTabs = true;

            var serviceLocator = this.GetServiceLocator();
            var typeFactory = serviceLocator.ResolveType<ITypeFactory>();
            _elementGenerator = typeFactory.CreateInstance<TabSpaceElementGenerator>();

            _textEditor.TextArea.TextView.ElementGenerators.Add(_elementGenerator);

            _textEditor.TextArea.SelectionChanged += OnTextAreaSelectionChanged;
            _textEditor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
            _textEditor.TextChanged += OnTextChanged;

            _highlightAllOccurencesOfSelectedWordTransformer = new HighlightAllOccurencesOfSelectedWordTransformer();
            _textEditor.TextArea.TextView.LineTransformers.Add(_highlightAllOccurencesOfSelectedWordTransformer);

            _textEditor.TextArea.TextView.LineTransformers.Add(new FirstLineAlwaysBoldTransformer());

            //SearchPanel.Install(_textEditor.TextArea);
            FindReplaceDialog.ShowForReplace(_textEditor);
        }

        private void OnTextChanged(object sender, EventArgs eventArgs)
        {
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
        #endregion

        #region Properties
        public bool IsDirty { get; set; }
        public bool HasSelection => _textEditor.SelectionLength > 0;
        public bool CanRedo => _textEditor.CanRedo;
        public bool CanUndo => _textEditor.CanUndo;
        #endregion

        #region Methods
        public event EventHandler<CaretTextLocationChangedEventArgs> CaretTextLocationChanged;
        public event EventHandler<EventArgs> TextChanged;

        public void Copy()
        {
            _textEditor.Copy();
        }

        public void Paste()
        {
            var text = Clipboard.GetText();
            text = text.Replace(Symbols.Comma.ToString(), string.Empty)
                .Replace(_elementGenerator.NewLine, string.Empty);

            var offset = _textEditor.CaretOffset;
            _textEditor.Document.Insert(offset, text);
        }

        public void Redo()
        {
            using (new DisposableToken<CsvTextEditorService>(this, x => x.Instance._isInRedoUndo = true, x =>
            {
                RefreshView();
                x.Instance._isInRedoUndo = false;
            }))
            {
                _textEditor.Redo();
            }
        }

        public void Undo()
        {
            using (new DisposableToken<CsvTextEditorService>(this, x => x.Instance._isInRedoUndo = true, x =>
            {
                RefreshView();
                x.Instance._isInRedoUndo = false;
            }))
            {
                _textEditor.Undo();
            }
        }

        public void DeleteSelectedText()
        {
        }

        public void Cut()
        {
            var selectedText = _textEditor.SelectedText;
            var textDocument = _textEditor.Document;

            var selectionStart = _textEditor.SelectionStart;
            var selectionLenght = _textEditor.SelectionLength;

            var newLine = _elementGenerator.NewLine;

            var text = textDocument.Text.RemoveCommaSeparatedText(selectionStart, selectionLenght, newLine);

            _textEditor.SelectionLength = 0;

            UpdateText(text);
            _textEditor.CaretOffset = selectionStart;

            Clipboard.SetText(selectedText);
        }

        public void AddColumn()
        {
            var textDocument = _textEditor.Document;
            var linesCount = textDocument.LineCount;
            var offset = _textEditor.CaretOffset;

            var affectedLocation = textDocument.GetLocation(offset);
            var columnNumberWithOffset = _elementGenerator.GetColumn(affectedLocation);

            var columnsCount = _elementGenerator.ColumnCount;
            var newLine = _elementGenerator.NewLine;

            var columnLenght = columnNumberWithOffset.Length;
            var columnOffset = columnNumberWithOffset.OffsetInLine;

            var lineIndex = affectedLocation.Line - 1;
            var columnIndex = columnNumberWithOffset.ColumnNumber + 1;

            if (affectedLocation.Column == columnOffset)
            {
                var oldText = textDocument.Text;
                var newText = oldText.InsertCommaSeparatedColumn(columnIndex, linesCount, columnsCount, newLine);

                UpdateText(newText);
                Goto(lineIndex, columnIndex);

                return;
            }

            if (affectedLocation.Column == columnOffset - columnLenght + 1)
            {
                columnIndex--;

                var oldText = textDocument.Text;
                var newText = oldText.InsertCommaSeparatedColumn(columnIndex, linesCount, columnsCount, newLine);

                UpdateText(newText);
                Goto(lineIndex, columnIndex);
            }
        }

        public void RemoveColumn()
        {
            var textDocument = _textEditor.Document;
            var linesCount = textDocument.LineCount;
            var offset = _textEditor.CaretOffset;

            var affectedLocation = textDocument.GetLocation(offset);
            var columnNumberWithOffset = _elementGenerator.GetColumn(affectedLocation);

            var columnsCount = _elementGenerator.ColumnCount;
            var newLine = _elementGenerator.NewLine;

            var lineIndex = affectedLocation.Line - 1;
            var columnIndex = columnNumberWithOffset.ColumnNumber;

            var text = _textEditor.Text.RemoveCommaSeparatedColumn(columnIndex, linesCount, columnsCount, newLine);

            UpdateText(text);
            Goto(lineIndex, columnIndex);
        }

        public void AddLine()
        {
            var offset = _textEditor.CaretOffset;
            var textDocument = _textEditor.Document;
            var affectedLocation = textDocument.GetLocation(offset);

            var nextLineIndex = affectedLocation.Line;
            var affectedColumn = affectedLocation.Column;
            var insertOffsetInLine = affectedColumn - 1;

            if (affectedColumn == 1)
            {
                nextLineIndex--;
            }

            var columnNumberWithOffset = _elementGenerator.GetColumn(affectedLocation);

            var columnNumber = columnNumberWithOffset.ColumnNumber;
            var columnOffset = columnNumberWithOffset.OffsetInLine;

            var columnsCount = _elementGenerator.ColumnCount;
            var newLine = _elementGenerator.NewLine;

            var caretColumnIndex = columnNumber;
            if (columnNumber == columnsCount - 1 && affectedColumn == columnOffset)
            {
                caretColumnIndex = 0;
            }

            var oldText = _textEditor.Text;
            var text = oldText.InsertLineWithTextTransfer(nextLineIndex, insertOffsetInLine, columnsCount, newLine);

            UpdateText(text);
            Goto(nextLineIndex, caretColumnIndex);
        }

        public void DuplicateLine()
        {
            var textDocument = _textEditor.Document;
            var offset = _textEditor.CaretOffset;

            var affectedLocation = textDocument.GetLocation(offset);
            var columnNumberWithOffset = _elementGenerator.GetColumn(affectedLocation);
            var newLine = _elementGenerator.NewLine;

            var lineIndex = affectedLocation.Line - 1;
            var columnIndex = columnNumberWithOffset.ColumnNumber;

            var line = textDocument.Lines[lineIndex];
            var lineOffset = line.Offset;
            var endlineOffset = line.NextLine?.Offset ?? line.EndOffset;

            var text = _textEditor.Text.DuplicateTextInLine(lineOffset, endlineOffset, newLine);

            UpdateText(text);
            Goto(lineIndex + 1, columnIndex);
        }

        public void RemoveLine()
        {
            var textDocument = _textEditor.Document;
            var offset = _textEditor.CaretOffset;

            var affectedLocation = textDocument.GetLocation(offset);
            var columnNumberWithOffset = _elementGenerator.GetColumn(affectedLocation);
            var newLine = _elementGenerator.NewLine;

            var lineIndex = affectedLocation.Line - 1;
            var columnIndex = columnNumberWithOffset.ColumnNumber;

            var line = textDocument.Lines[lineIndex];
            var lineOffset = line.Offset;
            var endlineOffset = line.NextLine?.Offset ?? line.EndOffset;

            var text = _textEditor.Text.RemoveText(lineOffset, endlineOffset, newLine);

            UpdateText(text);

            Goto(lineIndex - 1, columnIndex);
        }

        public void RefreshView()
        {
            _elementGenerator.Refresh(_textEditor.Text);
            _textEditor.TextArea.TextView.Redraw();
        }

        public void Initialize(string text)
        {
            UpdateText(text);

            _textEditor.Document.UndoStack.ClearAll();
        }

        public void RefreshLocation(int offset, int length)
        {
            if (_isInCustomUpdate || _isInRedoUndo)
            {
                return;
            }

            var textDocument = _textEditor.Document;
            var affectedLocation = textDocument.GetLocation(offset);

            if (_elementGenerator.RefreshLocation(affectedLocation, length))
            {
                _textEditor.TextArea.TextView.Redraw();
            }
        }

        public void UpdateText(string text)
        {
            text = text ?? string.Empty;

            _elementGenerator.Refresh(text);

            _isInCustomUpdate = true;

            using (_textEditor.Document.RunUpdate())
            {
                _textEditor.Document.Text = text;
            }

            _isInCustomUpdate = false;
        }

        public void GotoNextColumn()
        {
            var textDocument = _textEditor.Document;
            var offset = _textEditor.CaretOffset;

            var affectedLocation = textDocument.GetLocation(offset);
            var columnNumberWithOffset = _elementGenerator.GetColumn(affectedLocation);

            var columnsCount = _elementGenerator.ColumnCount;
            var nextColumnIndex = columnNumberWithOffset.ColumnNumber + 1;
            var lineIndex = affectedLocation.Line - 1;
            var nextLineIndex = lineIndex + 1;

            if (nextColumnIndex == columnsCount)
            {
                var linesCount = textDocument.LineCount;
                if (nextLineIndex == linesCount - 1)
                {
                    return;
                }

                Goto(nextLineIndex, 0);
            }

            Goto(lineIndex, nextColumnIndex);
        }

        public void GotoPreviousColumn()
        {
            var textDocument = _textEditor.Document;
            var offset = _textEditor.CaretOffset;

            var affectedLocation = textDocument.GetLocation(offset);
            var columnNumberWithOffset = _elementGenerator.GetColumn(affectedLocation);

            var columnIndex = columnNumberWithOffset.ColumnNumber;
            var previousColumnIndex = columnIndex > 0 ? columnIndex - 1 : -1;

            var lineIndex = affectedLocation.Line - 1;
            var previousLineIndex = lineIndex > 0 ? lineIndex - 1 : -1;

            if (previousColumnIndex == -1)
            {
                if (previousLineIndex == -1)
                {
                    return;
                }

                var columnsCount = _elementGenerator.ColumnCount;
                Goto(previousLineIndex, columnsCount - 1);
            }

            Goto(lineIndex, previousColumnIndex);
        }
        #endregion

        private void OnCaretPositionChanged(object sender, EventArgs eventArgs)
        {
            var offset = _textEditor.CaretOffset;
            var textDocument = _textEditor.Document;
            var currentTextLocation = textDocument.GetLocation(offset);
            var columnNumberWithOffset = _elementGenerator.GetColumn(currentTextLocation);
            var column = columnNumberWithOffset.ColumnNumber + 1;
            var line = currentTextLocation.Line + 1;

            if (_previousCaretColumn != column || _previousCaretLine != line)
            {
                CaretTextLocationChanged?.Invoke(this, new CaretTextLocationChangedEventArgs(column, line));

                _previousCaretColumn = column;
                _previousCaretLine = line;
            }
        }

        private void Goto(int lineIndex, int columnIndex)
        {
            _textEditor.SetCaretToSpecificLineAndColumn(lineIndex, columnIndex, _elementGenerator.Lines);
        }

        private void OnTextAreaSelectionChanged(object sender, EventArgs e)
        {
            _commandManager.InvalidateCommands();

            // Disable this line if the user is using the "Find Replace" dialog box
            _highlightAllOccurencesOfSelectedWordTransformer.SelectedWord = _textEditor.SelectedText;
            _highlightAllOccurencesOfSelectedWordTransformer.Selection = _textEditor.TextArea.Selection;

            RefreshView();
        }
    }
}